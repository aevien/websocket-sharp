using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  public sealed class PublicApiSnapshotTests
  {
    [Test]
    public void PublicApiMatchesSnapshot ()
    {
      var actual = BuildPublicApiSnapshot (typeof (WebSocket).Assembly);
      var path = Path.Combine (
        TestContext.CurrentContext.TestDirectory,
        "PublicApiSnapshot.txt"
      );
      var expected = File.ReadAllText (path).Replace ("\r\n", "\n");

      Assert.That (actual, Is.EqualTo (expected));
    }

    internal static string BuildPublicApiSnapshot (Assembly assembly)
    {
      var bindingFlags = BindingFlags.DeclaredOnly
                         | BindingFlags.Instance
                         | BindingFlags.Public
                         | BindingFlags.Static;
      var snapshot = new StringBuilder ();

      foreach (var type in assembly.GetExportedTypes ().OrderBy (t => t.FullName, StringComparer.Ordinal)) {
        snapshot.AppendFormat ("type {0} {1}\n", GetTypeKind (type), type.FullName);

        foreach (var field in type.GetFields (bindingFlags).OrderBy (FormatField, StringComparer.Ordinal))
          snapshot.AppendFormat ("  field {0}\n", FormatField (field));

        foreach (var ctor in type.GetConstructors (bindingFlags).OrderBy (FormatConstructor, StringComparer.Ordinal))
          snapshot.AppendFormat ("  ctor {0}\n", FormatConstructor (ctor));

        foreach (var property in type.GetProperties (bindingFlags).OrderBy (FormatProperty, StringComparer.Ordinal))
          snapshot.AppendFormat ("  property {0}\n", FormatProperty (property));

        foreach (var ev in type.GetEvents (bindingFlags).OrderBy (FormatEvent, StringComparer.Ordinal))
          snapshot.AppendFormat ("  event {0}\n", FormatEvent (ev));

        foreach (var method in type.GetMethods (bindingFlags)
                                   .Where (m => !m.IsSpecialName)
                                   .OrderBy (FormatMethod, StringComparer.Ordinal))
          snapshot.AppendFormat ("  method {0}\n", FormatMethod (method));

        if (type.IsEnum) {
          foreach (var name in Enum.GetNames (type).OrderBy (name => name, StringComparer.Ordinal))
            snapshot.AppendFormat ("  enum {0} = {1}\n", name, Convert.ToInt64 (Enum.Parse (type, name)));
        }
      }

      return snapshot.ToString ();
    }

    private static string FormatType (Type type)
    {
      if (type == null)
        return String.Empty;

      if (type.IsByRef)
        return FormatType (type.GetElementType ()) + "&";

      if (type.IsPointer)
        return FormatType (type.GetElementType ()) + "*";

      if (type.IsArray)
        return FormatType (type.GetElementType ()) + "[]";

      if (type.IsGenericParameter)
        return type.Name;

      if (!type.IsGenericType)
        return type.FullName ?? type.Name;

      var name = type.GetGenericTypeDefinition ().FullName;
      var tick = name.IndexOf ('`');

      if (tick >= 0)
        name = name.Substring (0, tick);

      var args = String.Join (
        ",",
        type.GetGenericArguments ().Select (FormatType).ToArray ()
      );

      return String.Format ("{0}<{1}>", name, args);
    }

    private static string FormatConstructor (ConstructorInfo ctor)
    {
      return String.Format (".ctor({0})", FormatParameters (ctor.GetParameters ()));
    }

    private static string FormatEvent (EventInfo ev)
    {
      return String.Format ("{0} {1}", FormatType (ev.EventHandlerType), ev.Name);
    }

    private static string FormatField (FieldInfo field)
    {
      return String.Format ("{0}{1} {2}", field.IsStatic ? "static " : String.Empty, FormatType (field.FieldType), field.Name);
    }

    private static string FormatMethod (MethodInfo method)
    {
      var genericArgs = method.IsGenericMethodDefinition
                        ? "<" + String.Join (",", method.GetGenericArguments ().Select (arg => arg.Name).ToArray ()) + ">"
                        : String.Empty;

      return String.Format (
        "{0} {1}{2}({3})",
        FormatType (method.ReturnType),
        method.Name,
        genericArgs,
        FormatParameters (method.GetParameters ())
      );
    }

    private static string FormatParameter (ParameterInfo parameter)
    {
      var type = parameter.ParameterType;
      var prefix = String.Empty;

      if (parameter.IsOut) {
        prefix = "out ";

        if (type.IsByRef)
          type = type.GetElementType ();
      }
      else if (type.IsByRef) {
        prefix = "ref ";
        type = type.GetElementType ();
      }

      if (Attribute.IsDefined (parameter, typeof (ParamArrayAttribute)))
        prefix = "params ";

      return prefix + FormatType (type);
    }

    private static string FormatParameters (ParameterInfo[] parameters)
    {
      return String.Join (",", parameters.Select (FormatParameter).ToArray ());
    }

    private static string FormatProperty (PropertyInfo property)
    {
      var indexParameters = property.GetIndexParameters ();
      var indexer = indexParameters.Length > 0
                    ? "[" + FormatParameters (indexParameters) + "]"
                    : String.Empty;

      return String.Format (
        "{0} {1}{2} {3}{4}",
        FormatType (property.PropertyType),
        property.Name,
        indexer,
        property.CanRead ? "get;" : String.Empty,
        property.CanWrite ? "set;" : String.Empty
      );
    }

    private static string GetTypeKind (Type type)
    {
      if (type.IsEnum)
        return "enum";

      if (type.IsInterface)
        return "interface";

      if (type.IsValueType)
        return "struct";

      if (typeof (Delegate).IsAssignableFrom (type))
        return "delegate";

      return type.IsAbstract && type.IsSealed
             ? "static-class"
             : type.IsAbstract
               ? "abstract-class"
               : "class";
    }
  }
}

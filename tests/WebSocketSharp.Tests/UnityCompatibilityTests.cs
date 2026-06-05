using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WebSocketSharp.Tests
{
  [TestFixture]
  public sealed class UnityCompatibilityTests
  {
    private static readonly ForbiddenPattern[] ForbiddenPatterns = {
      new ForbiddenPattern (ForbiddenCall ("Begin", "Invoke"), "delegate " + "Begin" + "Invoke" + " is not supported on Unity IL2CPP"),
      new ForbiddenPattern (ForbiddenCall ("End", "Invoke"), "delegate " + "End" + "Invoke" + " is not supported on Unity IL2CPP"),
      new ForbiddenPattern (@"\bReflection\.Emit\b", "runtime code generation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bAssemblyBuilder\b", "runtime assembly generation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bTypeBuilder\b", "runtime type generation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bDynamicMethod\b", "runtime method generation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bExpression\s*\.\s*Compile\s*\(", "runtime expression compilation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bThread\s*\.\s*Abort\s*\(", "Thread.Abort is unsafe and unsupported on modern Unity profiles"),
      new ForbiddenPattern (@"\bThread\s*\.\s*Suspend\s*\(", "Thread.Suspend is unsafe and unsupported on modern Unity profiles"),
      new ForbiddenPattern (@"\bThread\s*\.\s*Resume\s*\(", "Thread.Resume is unsafe and unsupported on modern Unity profiles"),
      new ForbiddenPattern (@"\bBinaryFormatter\b", "BinaryFormatter is obsolete and unsafe"),
      new ForbiddenPattern (@"\bNetDataContractSerializer\b", "NetDataContractSerializer is not appropriate for this Unity plugin"),
      new ForbiddenPattern (@"\bObjectStateFormatter\b", "ObjectStateFormatter is not appropriate for this Unity plugin"),
      new ForbiddenPattern (@"\bFormatterServices\b", "FormatterServices bypasses normal construction and is risky under AOT"),
      new ForbiddenPattern (@"\bDllImport(?:Attribute)?\b", "P/Invoke should not be introduced into this managed Unity plugin"),
      new ForbiddenPattern (@"\bCodeDomProvider\b", "runtime compilation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bCSharpCodeProvider\b", "runtime compilation is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bAppDomain\s*\.\s*CreateDomain\s*\(", "dynamic AppDomain usage is not suitable for Unity"),
      new ForbiddenPattern (@"\bAssembly\s*\.\s*LoadFile\s*\(", "runtime assembly loading is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bAssembly\s*\.\s*LoadFrom\s*\(", "runtime assembly loading is not suitable for Unity IL2CPP"),
      new ForbiddenPattern (@"\bRemotingServices\b", ".NET Remoting is obsolete and not suitable for Unity")
    };

    [Test]
    public void LibrarySourcesAvoidKnownUnityIl2CppIncompatibilities ()
    {
      var sourceRoot = Path.Combine (FindRepositoryRoot (), "websocket-sharp");
      var violations = new List<string> ();

      foreach (var file in Directory.GetFiles (sourceRoot, "*.cs", SearchOption.AllDirectories)) {
        var text = File.ReadAllText (file);
        var lines = text.Split (new[] { "\r\n", "\n" }, StringSplitOptions.None);

        foreach (var pattern in ForbiddenPatterns) {
          for (var i = 0; i < lines.Length; i++) {
            if (!pattern.Regex.IsMatch (lines[i]))
              continue;

            violations.Add (
              String.Format (
                "{0}:{1}: {2}",
                MakeRelativePath (sourceRoot, file),
                i + 1,
                pattern.Reason
              )
            );
          }
        }
      }

      Assert.That (
        violations,
        Is.Empty,
        String.Join (Environment.NewLine, violations.ToArray ())
      );
    }

    private static string FindRepositoryRoot ()
    {
      var dir = new DirectoryInfo (TestContext.CurrentContext.TestDirectory);

      while (dir != null) {
        var candidate = Path.Combine (dir.FullName, "websocket-sharp", "WebSocket.cs");

        if (File.Exists (candidate))
          return dir.FullName;

        dir = dir.Parent;
      }

      Assert.Fail ("Could not locate the repository root from the test directory.");
      return null;
    }

    private static string ForbiddenCall (string firstPart, string secondPart)
    {
      return @"\b" + firstPart + secondPart + @"\s*\(";
    }

    private static string MakeRelativePath (string root, string path)
    {
      var prefix = root.TrimEnd (Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

      return path.StartsWith (prefix, StringComparison.OrdinalIgnoreCase)
             ? path.Substring (prefix.Length)
             : path;
    }

    private sealed class ForbiddenPattern
    {
      public ForbiddenPattern (string pattern, string reason)
      {
        Regex = new Regex (pattern, RegexOptions.Compiled);
        Reason = reason;
      }

      public string Reason { get; private set; }

      public Regex Regex { get; private set; }
    }
  }
}

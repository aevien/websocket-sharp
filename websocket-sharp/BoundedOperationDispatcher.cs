#region License
/*
 * BoundedOperationDispatcher.cs
 *
 * The MIT License
 *
 * Copyright (c) 2026 aevien
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace WebSocketSharp
{
  internal sealed class BoundedOperationDispatcher : IDisposable
  {
    private sealed class WorkItem
    {
      internal WorkItem (Action execute, Action cancelWithoutBlocking)
      {
        Execute = execute;
        CancelWithoutBlocking = cancelWithoutBlocking;
      }

      internal Action CancelWithoutBlocking { get; private set; }

      internal Action Execute { get; private set; }
    }

    private readonly HashSet<WorkItem> _active;
    private readonly Action<Exception> _error;
    private readonly int _maxConcurrency;
    private readonly int _maxPending;
    private readonly string _name;
    private readonly Queue<WorkItem> _pending;
    private readonly object _sync;
    private readonly List<Thread> _workers;
    private bool _stopInProgress;
    private int _stopOwnerThreadId;
    private bool _stopping;
    private int _workerSequence;

    internal BoundedOperationDispatcher (
      int maxConcurrency,
      int maxPending,
      string name,
      Action<Exception> error
    )
    {
      if (maxConcurrency < 1)
        throw new ArgumentOutOfRangeException ("maxConcurrency");

      if (maxPending < 1)
        throw new ArgumentOutOfRangeException ("maxPending");

      _maxConcurrency = maxConcurrency;
      _maxPending = maxPending;
      _name = name ?? "BoundedOperationDispatcher";
      _error = error;

      _active = new HashSet<WorkItem> ();
      _pending = new Queue<WorkItem> ();
      _sync = new object ();
      _workers = new List<Thread> (maxConcurrency);
    }

    internal int ActiveCount {
      get {
        lock (_sync)
          return _active.Count;
      }
    }

    internal int PendingCount {
      get {
        lock (_sync)
          return _pending.Count;
      }
    }

    internal bool TryEnqueue (Action execute, Action cancelWithoutBlocking)
    {
      if (execute == null)
        throw new ArgumentNullException ("execute");

      if (cancelWithoutBlocking == null)
        throw new ArgumentNullException ("cancelWithoutBlocking");

      var item = new WorkItem (execute, cancelWithoutBlocking);
      var accepted = false;
      Exception workerError = null;

      lock (_sync) {
        if (!_stopping && _pending.Count < _maxPending) {
          _pending.Enqueue (item);
          accepted = true;

          var requiredWorkers = Math.Min (
                                  _maxConcurrency,
                                  _active.Count + _pending.Count
                                );

          if (_workers.Count < requiredWorkers
              && !tryStartWorker (out workerError)
              && _workers.Count == 0) {
            _pending.Dequeue ();
            accepted = false;
          }

          if (accepted)
            Monitor.Pulse (_sync);
        }
      }

      if (workerError != null)
        reportError (workerError);

      if (!accepted)
        cancelWorkItem (item);

      return accepted;
    }

    internal bool Stop (int millisecondsTimeout)
    {
      if (millisecondsTimeout < 0)
        millisecondsTimeout = 0;

      var elapsed = Stopwatch.StartNew ();
      WorkItem[] items = new WorkItem[0];
      Thread[] workers;

      lock (_sync) {
        var currentThreadId = Thread.CurrentThread.ManagedThreadId;

        while (_stopInProgress) {
          if (_stopOwnerThreadId == currentThreadId)
            return false;

          var remaining = millisecondsTimeout - (int) elapsed.ElapsedMilliseconds;

          if (remaining <= 0 || !Monitor.Wait (_sync, remaining))
            return false;
        }

        _stopInProgress = true;
        _stopOwnerThreadId = currentThreadId;

        if (!_stopping) {
          _stopping = true;

          var cancel = new List<WorkItem> (_pending.Count + _active.Count);

          cancel.AddRange (_active);

          while (_pending.Count > 0)
            cancel.Add (_pending.Dequeue ());

          items = cancel.ToArray ();
        }

        workers = _workers.ToArray ();

        Monitor.PulseAll (_sync);
      }

      try {
        foreach (var item in items)
          cancelWorkItem (item);

        foreach (var worker in workers) {
          if (worker == Thread.CurrentThread)
            continue;

          var remaining = millisecondsTimeout - (int) elapsed.ElapsedMilliseconds;

          if (remaining <= 0)
            break;

          try {
            worker.Join (remaining);
          }
          catch (ThreadStateException) {
          }
        }
      }
      finally {
        lock (_sync) {
          _stopInProgress = false;
          _stopOwnerThreadId = 0;
          Monitor.PulseAll (_sync);
        }
      }

      foreach (var worker in workers) {
        if (worker.IsAlive)
          return false;
      }

      return true;
    }

    private void cancelWorkItem (WorkItem item)
    {
      try {
        // Shutdown calls this synchronously, so the callback must only signal
        // cancellation or close the owned connection without waiting for Execute.
        item.CancelWithoutBlocking ();
      }
      catch (Exception ex) {
        reportError (ex);
      }
    }

    private void reportError (Exception exception)
    {
      if (_error == null)
        return;

      try {
        _error (exception);
      }
      catch {
      }
    }

    private void runWorker ()
    {
      while (true) {
        WorkItem item;

        lock (_sync) {
          while (_pending.Count == 0 && !_stopping) {
            Monitor.Wait (_sync);
          }

          if (_stopping && _pending.Count == 0)
            return;

          item = _pending.Dequeue ();
          _active.Add (item);
        }

        try {
          item.Execute ();
        }
        catch (Exception ex) {
          reportError (ex);
        }
        finally {
          lock (_sync) {
            _active.Remove (item);

            if (_stopping && _active.Count == 0)
              Monitor.PulseAll (_sync);
          }
        }
      }
    }

    private bool tryStartWorker (out Exception error)
    {
      error = null;
      Thread worker = null;

      try {
        worker = new Thread (runWorker);
        worker.IsBackground = true;
        worker.Name = String.Format ("{0}-{1}", _name, ++_workerSequence);

        _workers.Add (worker);
        worker.Start ();

        return true;
      }
      catch (Exception ex) {
        if (worker != null)
          _workers.Remove (worker);

        error = ex;
        return false;
      }
    }

    void IDisposable.Dispose ()
    {
      Stop (5000);
    }
  }
}

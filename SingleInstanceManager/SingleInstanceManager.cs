using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SingleInstanceManager
{
    /// <summary>
    ///     Provides a Manager for Single instance Applications.
    /// </summary>
    /// <example>
    ///     This examples shows, how to use a this manager in a single applications main:
    ///     <code>
    /// public static int Main(string[] args){
    ///     // submit an optional guid. If no parameter is given the entry assembly name is used.
    ///     var instanceManager = SingleInstanceManager.CreateManager("{GUID}");
    ///     if(instanceManager.RunApplication(args)){
    ///         // Register an event handler for second instances
    ///         instanceManager.SecondInstanceStarted + = OnSecondInstanceStarted;
    ///         // perform other bootstrap operations
    ///         // ...
    ///         // Close the manager, so other instances can start in a valid state       
    ///         instanceManager.Shutdown();
    ///     }
    ///     // perform exit logic
    /// }
    /// </code>
    /// </example>
    public class SingleInstanceManager : IDisposable
    {
        private const string GlobalNamespace = "Global";
        private const string LocalNamespace = "Local";
        private static SingleInstanceManager? _instance;
        private readonly CancellationTokenSource _cts;
        private readonly Mutex _instanceLockerMutex;
        private readonly MutexContext _mutexContext = new MutexContext();
        private readonly string _pipeName;
        private bool _disposed;
        private int _reentranceCounter;

        private SingleInstanceManager(string? guid, bool global = false)
        {
            // create a (hopefully) unique mutex name
            string assemblyName = guid ?? Assembly.GetEntryAssembly()?.FullName ?? "SingleInstanceManager";

            // this mutex will be shared across the system to signal an existing instance
            _instanceLockerMutex = new Mutex(false, $"{(global ? GlobalNamespace : LocalNamespace)}\\{assemblyName}");

            // create a (hopefully) unique lock name
            _pipeName = assemblyName + "argsStream";

            _cts = new CancellationTokenSource();
        }

        public event EventHandler<SecondInstanceStartupEventArgs>? SecondInstanceStarted;

        public static SingleInstanceManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    Interlocked.CompareExchange(ref _instance, new SingleInstanceManager(null), null);
                }

                return _instance;
            }
            private set
            {
                if (Interlocked.CompareExchange(ref _instance, value, null) != null)
                {
                    throw new InvalidOperationException("There is already an instance of the single instance manager");
                }
            }
        }

        public SynchronizationContext? SynchronizationContext { get; set; } = SynchronizationContext.Current;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _mutexContext.Execute(
                () =>
                {
                    _instanceLockerMutex?.Dispose();
                    Interlocked.Decrement(ref _reentranceCounter);
                });

            _mutexContext.Dispose();
            _cts?.Dispose();
            _instance = null;
        }

        public static SingleInstanceManager CreateManager(string? guid = null, bool global = false)
        {
            return Instance = new SingleInstanceManager(guid, global);
        }

        public bool RunApplication(string[] args)
        {
            try
            {
                // if WaitOne returns true, no other instance has taken the mutex
                // All calls to the mutex are made through _mutex context, to ensure
                // we release the lock on the same thread, as we locked earlier
                bool acquiredLock = _mutexContext.Execute(LockMutex);

                if (acquiredLock)
                {
                    Task.Factory.StartNew(() => ConnectionLoop(_cts.Token), TaskCreationOptions.LongRunning);
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                Task.Factory.StartNew(() => ConnectionLoop(_cts.Token), TaskCreationOptions.LongRunning);
                return true;
            }

            // connect to the existing instance using a named pipe
            NamedPipeClientStream client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect();

            // write command line params to other instance.
            // We use a simple protocol here:
            // Send an integer indicating the count of parameters
            // Send each parameter as length prefixed unicode string.
            using BinaryWriter writer = new BinaryWriter(client, Encoding.Unicode);
            writer.WriteArray(args);

            writer.Write(0);
            writer.Write(Environment.CurrentDirectory);

            IDictionary environmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
            writer.WriteDictionary(environmentVariables);

            client.WaitForPipeDrain();

            return false;
        }

        public void Shutdown()
        {
            Dispose();
        }

        private async void ConnectionLoop(CancellationToken cancellationToken)
        {
            SemaphoreSlim serverCounter = new SemaphoreSlim(2);

            void DoConnection(BinaryReader reader)
            {
                try
                {
                    string[] args = reader.ReadArray();
                    _ = reader.ReadInt32();
                    string commandLine = reader.ReadString();
                    IReadOnlyDictionary<string, string> environmentVariables = reader.ReadDictionary();
                    OnSecondInstanceStarted(args, environmentVariables, commandLine);
                }
                finally
                {
                    reader.Dispose();
                    serverCounter.Release();
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await serverCounter.WaitAsync(cancellationToken).ConfigureAwait(false);
                // Create a listeners on the pipe
                NamedPipeServerStream server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    2,
                    PipeTransmissionMode.Message);
                BinaryReader reader = new BinaryReader(server, Encoding.Unicode, false);
                try
                {
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    server.Dispose();
                    serverCounter.Release();
                    continue;
                }

                _ = Task.Run(() => DoConnection(reader), cancellationToken);
            }
        }

        private bool LockMutex()
        {
            if (!_instanceLockerMutex.WaitOne(0))
            {
                return false;
            }

            // In normal scenarios the WaitOne call above is sufficient,
            // but in a scenario, like our unit cases, the forcing all mutex calls
            // to a single thread causes a problem, because mutex is reentrant we 
            // could lock on the same mutex more than once. So we have to count
            // on ourself and limit the lock count to one.
            if (Interlocked.Increment(ref _reentranceCounter) > 1)
            {
                Interlocked.Decrement(ref _reentranceCounter);
                _instanceLockerMutex.ReleaseMutex();
                return false;
            }

            return true;
        }

        private void OnSecondInstanceStarted(
            string[] parameters,
            IReadOnlyDictionary<string, string> environmentalVariables,
            string workingDirectory)
        {
            if (SecondInstanceStarted is not { } secondInstanceStarted)
            {
                return;
            }

            SecondInstanceStartupEventArgs eventArgs = new SecondInstanceStartupEventArgs(
                parameters,
                environmentalVariables,
                workingDirectory);

            void RaiseSecondInstanceStarted(object o)
            {
                try
                {
                    secondInstanceStarted.Invoke(this, eventArgs);
                }
                catch (Exception)
                {
                    // ignored
                    // we ignore all exceptions from raising the SecondInstanceStarted to prevent starvation of a connection.
                }
            }

            if (SynchronizationContext != null)
            {
                SynchronizationContext.Post(RaiseSecondInstanceStarted, null);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(RaiseSecondInstanceStarted, null);
            }
        }
    }
}

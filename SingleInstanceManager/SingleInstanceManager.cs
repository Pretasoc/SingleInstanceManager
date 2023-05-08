using System;
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
        private readonly string _pipeName;

        private SingleInstanceManager(string? guid, bool global = false)
        {
            // create a (hopefully) unique mutex name
            string assemblyName = guid ?? Assembly.GetEntryAssembly()?.FullName ?? "SingleInstanceManager";

            // this mutex will be shared across the system to signal an existing instance
            _instanceLockerMutex = new Mutex(true, $"{(global ? GlobalNamespace : LocalNamespace)}\\{assemblyName}");

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
            _instanceLockerMutex?.Dispose();

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
                if (_instanceLockerMutex.WaitOne(0))
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
            using BinaryWriter writer = new BinaryWriter(client);
            writer.Write(args.Length);

            foreach (string arg in args)
            {
                writer.Write(arg);
            }

            client.WaitForPipeDrain();

            return false;
        }

        public void Shutdown()
        {
            Dispose();
        }

        private async void ConnectionLoop(CancellationToken cancellationToken)
        {
            void DoConnection(BinaryReader reader)
            {
                try
                {
                    int argNumber = reader.ReadInt32();
                    string[] args = new string[argNumber];

                    for (int i = 0; i < argNumber; i++)
                    {
                        args[i] = reader.ReadString();
                    }

                    OnSecondInstanceStarted(args);
                }
                finally
                {
                    reader.Dispose();
                }
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                // Create a listeners on the pipe
                NamedPipeServerStream server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    2,
                    PipeTransmissionMode.Message);
                BinaryReader reader = new BinaryReader(server, Encoding.Default, false);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => DoConnection(reader), cancellationToken);
            }
        }

        private void OnSecondInstanceStarted(string[] e)
        {
            SecondInstanceStartupEventArgs eventArgs = new SecondInstanceStartupEventArgs(e);
            if ((SynchronizationContext != null) && SecondInstanceStarted is { } secondInstanceHandler)
            {
                SynchronizationContext.Post(
                    state => secondInstanceHandler.Invoke(null, (SecondInstanceStartupEventArgs)state),
                    eventArgs);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(o => SecondInstanceStarted?.Invoke(null, (SecondInstanceStartupEventArgs)o), eventArgs);
            }
        }
    }
}

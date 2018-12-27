using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SingleInstanceManager
{
    /// <summary>
    /// Provides a Manager for Single instance Applications.
    /// </summary>
    /// <example>
    /// This examples shows, how to use a this manager in a single applications main:
    /// <code>
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
        private readonly Mutex _instanceLockerMutex;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cts;
        private readonly SemaphoreSlim _serverSemaphore = new SemaphoreSlim(2);
        private readonly TaskFactory _serverFactory;



        public event EventHandler<SecondInstanceStartupEventArgs> SecondInstanceStarted;

        public static SingleInstanceManager Instance { get; private set; }

        private SingleInstanceManager(string guid)
        {
            // create a (hopefully) unique mutex name
            var assemblyName = guid ?? Assembly.GetEntryAssembly()?.FullName ?? "SingleInstanceManager";

            // this mutex will be shared across the system to signal an existing instance
            _instanceLockerMutex = new Mutex(true, assemblyName);

            // create a (hopefully) unique lock name
            _pipeName = assemblyName + "argsStream";

            _cts = new CancellationTokenSource();

            // start manager to listen for second applications
            _serverFactory = new TaskFactory(_cts.Token);


        }

        public static SingleInstanceManager CreateManager(string guid = null)
        {
            var i = new SingleInstanceManager(guid);
            Instance = i;
            return i;
        }


        public void Shutdown()
        {
            _instanceLockerMutex.ReleaseMutex();
            _cts.Cancel();
        }

        public bool RunApplication(string[] args)
        {
            // if WaitOne returns true, no other instance has taken the mutex
            if (_instanceLockerMutex.WaitOne(0))
            {

                Task.Run(() => HandleConnection(_cts.Token));

                return true;
            }

            // connect to the existing instnace using a named pipe
            var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect();

            // write command line params to other instance.
            // We use a simple protocoll here:
            // Send an integer indicating the count of parameters
            // Send each parameter as length prefixed unicode string.
            using (var writer = new BinaryWriter(client))
            {
                writer.Write(args.Length);

                foreach (var arg in args)
                {
                    writer.Write(arg);
                }

                client.WaitForPipeDrain();
            }

            return false;
        }

        private async Task HandleConnection(CancellationToken cancellationToken)
        {
            // Not more than two waiting servers
            await _serverSemaphore.WaitAsync(cancellationToken);

            // Create a listerner on the pipe
            var server = new NamedPipeServerStream(_pipeName, PipeDirection.In,2,PipeTransmissionMode.Message);
            var reader = new BinaryReader(server);

            // it is intendet not to wait. We will spawn a new Server before Accepting a connection
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _serverFactory.StartNew(() => HandleConnection(cancellationToken), cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            await server.WaitForConnectionAsync(cancellationToken);

            // This server is now busy, allow an other to go into waiting state
            _serverSemaphore.Release();

            // read the parameters as defined above
            var argNumber = reader.ReadInt32();
            var args = new string[argNumber];

            for (var i = 0; i < argNumber; i++)
            {
                args[i] = reader.ReadString();
            }

            // raise the event
            OnSecondInstanceStarted(args);

            // close reader & server to free resources
            // otherwise, the inter process communication will only work twice...
            reader.Close();
            server.Close();

        }




        private void OnSecondInstanceStarted(string[] e)
        {
            SecondInstanceStarted?.Invoke(null, new SecondInstanceStartupEventArgs(e));
        }

        public void Dispose()
        {
            _instanceLockerMutex?.Dispose();

            _cts?.Dispose();
        }
    }
}

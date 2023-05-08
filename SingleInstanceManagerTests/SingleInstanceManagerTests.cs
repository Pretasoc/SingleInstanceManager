using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SingleInstanceManager.Test
{
    [TestFixture]
    public class SingleInstanceManagerTests
    {
        [Test]
        [NonParallelizable]
        public void ExceptionOnEvent([Range(1, 5)] int numThreads)
        {
            AppDomain.CurrentDomain.UnhandledException += DoNothing;
            using SingleInstanceManager manager = SingleInstanceManager.CreateManager(Guid.NewGuid().ToString("N"));

            bool start1 = manager.RunApplication(new string[] { });
            Assert.AreEqual(true, start1);

            manager.SecondInstanceStarted += (sender, e) => throw new Exception();

            Thread[] threads = new Thread[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                Thread t = new Thread(
                    () =>
                    {
                        bool start2 = manager.RunApplication(new string[] { });
                        Assert.AreEqual(false, start2);
                    });

                t.Start();
                threads[i] = t;
            }

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            manager.Shutdown();

            AppDomain.CurrentDomain.UnhandledException -= DoNothing;
        }

        [Test]
        [NonParallelizable]
        public void RunApplicationTest()
        {
            string[] parameters = new[] { "a", "b", "longParam with spaces and ` \x305 other chars" };
            using SingleInstanceManager manager = SingleInstanceManager.CreateManager(Guid.NewGuid().ToString("N"));

            bool start1 = manager.RunApplication(new string[] { });
            Assert.AreEqual(true, start1);

            Barrier m = new Barrier(2);
            manager.SecondInstanceStarted += (sender, e) =>
            {
                CollectionAssert.AreEqual(parameters, e.CommandLineParameters);
                m.SignalAndWait();
                m.Dispose();
            };

            Thread t = new Thread(
                () =>
                {
                    bool start2 = manager.RunApplication(parameters);
                    Assert.AreEqual(false, start2);
                });

            t.Start();
            t.Join();

            if (!m.SignalAndWait(10000))
            {
                Assert.Warn("Signal timed out");
            }

            ;
            manager.Shutdown();
        }

        [Test]
        [NonParallelizable]
        public async Task SimpleRunApplicationTest()
        {
            using SingleInstanceManager manager = SingleInstanceManager.CreateManager(Guid.NewGuid().ToString("N"));

            bool start1 = manager.RunApplication(Array.Empty<string>());
            Assert.AreEqual(true, start1);

            bool secondSignaled = false;
            manager.SecondInstanceStarted += (sender, e) => { secondSignaled = true; };

            await Task.Run(
                () =>
                {
                    bool start2 = manager.RunApplication(Array.Empty<string>());
                    Assert.AreEqual(false, start2);
                });

            await Task.Delay(100);

            Assert.That(secondSignaled);
            manager.Shutdown();
        }

        private void DoNothing(object sender, UnhandledExceptionEventArgs e)
        {
        }
    }
}

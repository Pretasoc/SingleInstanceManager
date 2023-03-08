﻿using NUnit.Framework;
using SingleInstanceManager;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SingleInstanceManager.Tests
{
    [TestFixture()]
    public class SingleInstanceManagerTests
    {
        [Test()]
        public void SimpleRunApplicationTest()
        {
            var manager = SingleInstanceManager.CreateManager("unitTest");

            var start1 = manager.RunApplication(Array.Empty<string>());
            Assert.AreEqual(true, start1);

            var secondSignaled = false;
            manager.SecondInstanceStarted += (sender, e) =>
            {
                secondSignaled = true;
            };

            var t = new Thread(() =>
            {
                var start2 = manager.RunApplication(Array.Empty<string>());
                Assert.AreEqual(false, start2);
            });


            t.Start();
            t.Join();

            Assert.That(secondSignaled);
            manager.Shutdown();
        }

        [Test]
        public void ExceptionOnEvent([Range(1, 5)] int numThreads)
        {
            var manager = SingleInstanceManager.CreateManager("unitTest");

            var start1 = manager.RunApplication(new string[] { });
            Assert.AreEqual(true, start1);

            manager.SecondInstanceStarted += (sender, e) => throw new Exception();

            Thread[] threads = new Thread[numThreads - 1];
            for (int i = 0; i < numThreads; i++)
            {
                var t = new Thread(() =>
                {
                    var start2 = manager.RunApplication(new string[] { });
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
        }

        [Test()]
        [NonParallelizable]
        public void RunApplicationTest()
        {
            var parameters = new[] {"a", "b", "longParam with spaces and ` \x305 other chars"};
            var manager = SingleInstanceManager.CreateManager("unitTest");

            var start1 = manager.RunApplication(new string[] { });
            Assert.AreEqual(true, start1);

            var m = new Barrier(2);
            manager.SecondInstanceStarted += (sender, e) =>
            {
                CollectionAssert.AreEqual(parameters, e.CommandLineParameters);
                m.SignalAndWait();
                m.Dispose();
            };

            var t = new Thread(() =>
            {
                var start2 = manager.RunApplication(parameters);
                Assert.AreEqual(false, start2);
            });


            t.Start();
            t.Join();

            if(!m.SignalAndWait(10000))
            {
                Assert.Warn("Signal timed out");
            };
            manager.Shutdown();

        }
    }
}
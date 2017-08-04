﻿using EventGen;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace DnDGen.Stress.Events.Tests
{
    [TestFixture]
    public class GenerateTests
    {
        private Stressor stressor;
        private Assembly runningAssembly;
        private StringBuilder console;
        private Mock<ClientIDManager> mockClientIdManager;
        private Mock<GenEventQueue> mockEventQueue;
        private Guid clientId;
        private Stopwatch stopwatch;

        [SetUp]
        public void Setup()
        {
            mockClientIdManager = new Mock<ClientIDManager>();
            mockEventQueue = new Mock<GenEventQueue>();
            runningAssembly = Assembly.GetExecutingAssembly();
            stressor = new StressorWithEvents(false, runningAssembly, mockClientIdManager.Object, mockEventQueue.Object, "Unit Test");
            stopwatch = new Stopwatch();
            console = new StringBuilder();
            var writer = new StringWriter(console);

            Console.SetOut(writer);

            clientId = Guid.Empty;
            var count = 1;

            mockClientIdManager.Setup(m => m.SetClientID(It.IsAny<Guid>())).Callback((Guid g) => clientId = g);
            mockEventQueue.Setup(q => q.DequeueAll(It.Is<Guid>(g => g == clientId))).Returns((Guid g) => new[]
            {
                new GenEvent("Unit Test", $"Event {count++} for {g}"),
                new GenEvent("Unit Test", $"Event {count++} for {g}"),
                new GenEvent("Wrong Source", $"Wrong event for {g}"),
            });
        }

        [TearDown]
        public void Teardown()
        {
            var standardOutput = new StreamWriter(Console.OpenStandardOutput());
            standardOutput.AutoFlush = true;
            Console.SetOut(standardOutput);
        }

        [Test]
        public void DoesNotStopWhenTimeLimitHit()
        {
            var count = 0;

            stopwatch.Start();
            var result = stressor.Generate(() => count++, c => c == 9266);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed, Is.GreaterThan(stressor.TimeLimit));
            Assert.That(result, Is.EqualTo(9266));
            Assert.That(count, Is.EqualTo(9267));
        }

        [Test]
        public void DoesNotStopWhenIterationLimitHit()
        {
            var count = 0;

            stopwatch.Start();
            var result = stressor.Generate(() => count++, c => c == Stressor.ConfidentIterations + 1);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed, Is.GreaterThan(stressor.TimeLimit));
            Assert.That(result, Is.EqualTo(Stressor.ConfidentIterations + 1));
            Assert.That(count, Is.EqualTo(Stressor.ConfidentIterations + 2));
        }

        [Test]
        public void StopsWhenGenerated()
        {
            var count = 0;

            stopwatch.Start();
            var result = stressor.Generate(() => count++, c => c > 42);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed, Is.LessThan(stressor.TimeLimit));
            Assert.That(result, Is.EqualTo(43));
            Assert.That(count, Is.EqualTo(44));
        }

        [Test]
        public void GenerateDoesNotWriteToConsole()
        {
            var count = 0;

            var result = stressor.Generate(() => count++, c => c > 9266);
            Assert.That(result, Is.EqualTo(9267));
            Assert.That(count, Is.EqualTo(9268));

            var output = console.ToString();
            Assert.That(output, Is.Empty);
        }

        [Test]
        public void Generate()
        {
            var count = 0;
            var result = stressor.Generate(() => count++, c => c == 9266);
            Assert.That(result, Is.EqualTo(9266));
        }

        [Test]
        public void GenerateWithinGenerateHonorsTimeLimit()
        {
            var count = 0;

            stopwatch.Start();
            var result = stressor.Generate(() => stressor.Generate(() => count++, c => c % 60 == 0), c => c == 600);
            stopwatch.Stop();

            Assert.That(stopwatch.Elapsed, Is.LessThanOrEqualTo(stressor.TimeLimit));
            Assert.That(result, Is.EqualTo(600));
            Assert.That(count, Is.EqualTo(601));
        }

        [Test]
        public void DoesNotSetClientIdForGenerateEvents()
        {
            var result = stressor.Generate(() => 1, i => i > 0);
            Assert.That(result, Is.EqualTo(1));
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void EventSpacingForGenerateIsWithin1SecondOfEachOther()
        {
            var events = new List<GenEvent>();
            events.Add(new GenEvent("Unit Test", "First Message") { When = DateTime.Now.AddMilliseconds(-999) });
            events.Add(new GenEvent("Unit Test", "Last Message") { When = DateTime.Now });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(events);

            var result = stressor.Generate(() => 1, i => i > 0);
            Assert.That(result, Is.EqualTo(1));

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void EventSpacingIsNotWithin1SecondOfEachOther()
        {
            var events = new List<GenEvent>();
            events.Add(new GenEvent("Unit Test", "First Message") { When = DateTime.Now.AddMilliseconds(-1001) });
            events.Add(new GenEvent("Unit Test", "Last Message") { When = DateTime.Now });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(events);

            Assert.That(() => stressor.Generate(() => 1, i => i > 0), Throws.InstanceOf<AssertionException>());

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void EventSpacingIsWithin1SecondOfEachOtherWithNonSourceEvents()
        {
            var events = new List<GenEvent>();
            events.Add(new GenEvent("Unit Test", "First Message") { When = DateTime.Now.AddMilliseconds(-1001) });
            events.Add(new GenEvent("Wrong Source", "Wrong Message") { When = DateTime.Now.AddMilliseconds(-999) });
            events.Add(new GenEvent("Unit Test", "Last Message") { When = DateTime.Now });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(events);

            var result = stressor.Generate(() => 1, i => i > 0);
            Assert.That(result, Is.EqualTo(1));

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void EventsAreOrderedChronologically()
        {
            var events = new List<GenEvent>();
            events.Add(new GenEvent("Unit Test", "First Message") { When = DateTime.Now.AddMilliseconds(-1) });
            events.Add(new GenEvent("Unit Test", "Last Message") { When = DateTime.Now });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(events);

            var result = stressor.Generate(() => 1, i => i > 0);
            Assert.That(result, Is.EqualTo(1));

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void EventsAreNotOrderedChronologically()
        {
            var events = new List<GenEvent>();
            events.Add(new GenEvent("Unit Test", "First Message") { When = DateTime.Now.AddMilliseconds(10) });
            events.Add(new GenEvent("Unit Test", "Last Message") { When = DateTime.Now });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(events);

            Assert.That(() => stressor.Generate(() => 1, i => i > 0), Throws.InstanceOf<AssertionException>());

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }

        [Test]
        public void DoNotAssertEventsBetweenGenerations()
        {
            var earlyEvents = new List<GenEvent>();
            earlyEvents.Add(new GenEvent("Unit Test", "First early message") { When = DateTime.Now.AddMilliseconds(-1000) });
            earlyEvents.Add(new GenEvent("Unit Test", "Last early message") { When = DateTime.Now.AddMilliseconds(-999) });

            var lateEvents = new List<GenEvent>();
            lateEvents.Add(new GenEvent("Unit Test", "First late message") { When = DateTime.Now.AddMilliseconds(999) });
            lateEvents.Add(new GenEvent("Unit Test", "Last late message") { When = DateTime.Now.AddMilliseconds(1000) });

            mockEventQueue.SetupSequence(q => q.DequeueAll(It.Is<Guid>(g => g == clientId)))
                .Returns(earlyEvents)
                .Returns(lateEvents);

            var count = 0;
            var result = stressor.Generate(() => count++, i => i == 1);
            Assert.That(result, Is.EqualTo(1));

            var output = console.ToString();
            Assert.That(output, Is.Empty);
            Assert.That(clientId, Is.EqualTo(Guid.Empty));
        }
    }
}
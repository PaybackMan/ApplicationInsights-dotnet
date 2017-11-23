﻿namespace Microsoft.ApplicationInsights.DependencyCollector
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.DependencyCollector.Implementation;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation;
#if !NETCORE
    using Microsoft.ApplicationInsights.Web.TestFramework;
#else
    using Microsoft.ApplicationInsights.Tests;
#endif
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class EventHubsDiagnosticListenerTests
    {
        private TelemetryConfiguration configuration;
        private List<ITelemetry> sentItems;

        [TestInitialize]
        public void TestInitialize()
        {
            this.configuration = new TelemetryConfiguration();
            this.sentItems = new List<ITelemetry>();
            this.configuration.TelemetryChannel = new StubTelemetryChannel { OnSend = item => this.sentItems.Add(item), EndpointAddress = "https://dc.services.visualstudio.com/v2/track" };
            this.configuration.InstrumentationKey = Guid.NewGuid().ToString();
        }

        [TestCleanup]
        public void CleanUp()
        {
            while (Activity.Current != null)
            {
                Activity.Current.Stop();
            }
        }

        [TestMethod]
        public void EventHubsSuccessfulSendIsHandled()
        {
            using (var module = new DependencyTrackingTelemetryModule())
            {
                module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.EventHubs");
                module.Initialize(this.configuration);

                DiagnosticListener listener = new DiagnosticListener("Microsoft.Azure.EventHubs");

                Activity parentActivity = new Activity("parent").AddBaggage("k1", "v1").Start();
                var telemetry  = this.TrackOperation<DependencyTelemetry>(listener, "Microsoft.Azure.EventHubs.Send", TaskStatus.RanToCompletion);

                Assert.IsNotNull(telemetry);
                Assert.AreEqual("Send", telemetry.Name);
                Assert.AreEqual(RemoteDependencyConstants.AzureEventHubs, telemetry.Type);
                Assert.AreEqual("sb://eventhubname.servicebus.windows.net/ | ehname", telemetry.Target);
                Assert.IsTrue(telemetry.Success.Value);

                Assert.AreEqual(parentActivity.Id, telemetry.Context.Operation.ParentId);
                Assert.AreEqual(parentActivity.RootId, telemetry.Context.Operation.Id);
                Assert.AreEqual("v1", telemetry.Properties["k1"]);
                Assert.AreEqual("eventhubname.servicebus.windows.net", telemetry.Properties["peer.hostname"]);
                Assert.AreEqual("ehname", telemetry.Properties["eh.event_hub_name"]);
                Assert.AreEqual("SomePartitionKeyHere", telemetry.Properties["eh.partition_key"]);
                Assert.AreEqual("EventHubClient1(ehname)", telemetry.Properties["eh.client_id"]);
            }
        }

        [TestMethod]
        public void EventHubsFailedSendIsHandled()
        {
            using (var module = new DependencyTrackingTelemetryModule())
            {
                module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.EventHubs");
                module.Initialize(this.configuration);

                DiagnosticListener listener = new DiagnosticListener("Microsoft.Azure.EventHubs");

                Activity parentActivity = new Activity("parent").AddBaggage("k1", "v1").Start();
                var telemetry = this.TrackOperation<DependencyTelemetry>(listener, "Microsoft.Azure.EventHubs.Send", TaskStatus.Faulted);

                Assert.IsNotNull(telemetry);
                Assert.AreEqual("Send", telemetry.Name);
                Assert.AreEqual(RemoteDependencyConstants.AzureEventHubs, telemetry.Type);
                Assert.AreEqual("sb://eventhubname.servicebus.windows.net/ | ehname", telemetry.Target);
                Assert.IsFalse(telemetry.Success.Value);

                Assert.AreEqual(parentActivity.Id, telemetry.Context.Operation.ParentId);
                Assert.AreEqual(parentActivity.RootId, telemetry.Context.Operation.Id);
                Assert.AreEqual("v1", telemetry.Properties["k1"]);
                Assert.AreEqual("eventhubname.servicebus.windows.net", telemetry.Properties["peer.hostname"]);
                Assert.AreEqual("ehname", telemetry.Properties["eh.event_hub_name"]);
                Assert.AreEqual("SomePartitionKeyHere", telemetry.Properties["eh.partition_key"]);
                Assert.AreEqual("EventHubClient1(ehname)", telemetry.Properties["eh.client_id"]);
            }
        }

        [TestMethod]
        public void EventHubsSendExceptionsAreIgnored()
        {
            using (var module = new DependencyTrackingTelemetryModule())
            {
                this.configuration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                module.IncludeDiagnosticSourceActivities.Add("Microsoft.Azure.EventHubs");
                module.Initialize(this.configuration);

                DiagnosticListener listener = new DiagnosticListener("Microsoft.Azure.EventHubs");

                Activity parentActivity = new Activity("parent").AddBaggage("k1", "v1").Start();
                if (listener.IsEnabled("Microsoft.Azure.EventHubs.Send.Exception"))
                {
                    listener.Write("Microsoft.Azure.EventHubs.Send.Exception", new { Exception = new Exception("123") });
                }

                Assert.IsFalse(this.sentItems.Any());
            }
        }

        private T TrackOperation<T>(DiagnosticListener listener, string activityName, TaskStatus status) where T : OperationTelemetry
        {
            Activity activity = null;

            if (listener.IsEnabled(activityName))
            {
                activity = new Activity(activityName);
                activity.AddTag("peer.hostname", "eventhubname.servicebus.windows.net");
                activity.AddTag("eh.event_hub_name", "ehname");
                activity.AddTag("eh.partition_key", "SomePartitionKeyHere");
                activity.AddTag("eh.client_id", "EventHubClient1(ehname)");
                if (listener.IsEnabled(activityName + ".Start"))
                {
                    listener.StartActivity(
                        activity,
                        new
                        {
                            Entity = "ehname",
                            Endpoint = new Uri("sb://eventhubname.servicebus.windows.net/"),
                            PartitionKey = "SomePartitionKeyHere"
                        });
                }
                else
                {
                    activity.Start();
                }
            }

            if (activity != null)
            {
                listener.StopActivity(
                    activity,
                    new
                    {
                        Entity = "ehname",
                        Endpoint = new Uri("sb://eventhubname.servicebus.windows.net/"),
                        PartitionKey = "SomePartitionKeyHere",
                        Status = status
                    });
                return this.sentItems.Last() as T;
            }

            return null;
        }
    }
}
using System;
using System.Net;
using PingTracer.Storage;
using Xunit;

namespace PingTracer.Tests
{
	public class RouteSnapshotTests
	{
		private static readonly DateTime T0 = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

		private static HopTimeSeries MakeSeries(string ip)
		{
			return new HopTimeSeries(0, IPAddress.Parse(ip), T0, startDnsLookup: false);
		}

		[Fact]
		public void DiffFrom_EmptyList_WhenIdentical()
		{
			var a = MakeSeries("1.2.3.4");
			var b = MakeSeries("1.2.3.4");

			var snap1 = new RouteSnapshot(T0, new HopTimeSeries[] { a });
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new HopTimeSeries[] { b });

			var diff = snap2.DiffFrom(snap1);
			Assert.Empty(diff);
		}

		[Fact]
		public void DiffFrom_DetectsAddressChange()
		{
			var old = MakeSeries("1.2.3.4");
			var newSeries = MakeSeries("5.6.7.8");

			var snap1 = new RouteSnapshot(T0, new HopTimeSeries[] { old });
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new HopTimeSeries[] { newSeries });

			var diff = snap2.DiffFrom(snap1);
			Assert.Single(diff);
			Assert.Equal(0, diff[0].HopIndex);
			Assert.Equal(IPAddress.Parse("1.2.3.4"), diff[0].OldAddress);
			Assert.Equal(IPAddress.Parse("5.6.7.8"), diff[0].NewAddress);
		}

		[Fact]
		public void DiffFrom_DetectsHopAppearing()
		{
			// Previous snapshot had no hops; new has one
			var snap1 = new RouteSnapshot(T0, new HopTimeSeries[0]);
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new HopTimeSeries[] { MakeSeries("1.2.3.4") });

			var diff = snap2.DiffFrom(snap1);
			Assert.Single(diff);
			Assert.Null(diff[0].OldAddress);
			Assert.Equal(IPAddress.Parse("1.2.3.4"), diff[0].NewAddress);
		}

		[Fact]
		public void DiffFrom_DetectsHopDisappearing()
		{
			var snap1 = new RouteSnapshot(T0, new HopTimeSeries[] { MakeSeries("1.2.3.4") });
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new HopTimeSeries[] { null });

			var diff = snap2.DiffFrom(snap1);
			Assert.Single(diff);
			Assert.Equal(IPAddress.Parse("1.2.3.4"), diff[0].OldAddress);
			Assert.Null(diff[0].NewAddress);
		}

		[Fact]
		public void DiffFrom_NullPrevious_AllHopsAreNew()
		{
			var snap = new RouteSnapshot(T0, new HopTimeSeries[]
			{
				MakeSeries("1.2.3.4"),
				null,
				MakeSeries("9.8.7.6"),
			});

			var diff = snap.DiffFrom(null);
			// Two non-null hops should appear as new
			Assert.Equal(2, diff.Count);
		}

		[Fact]
		public void DiffFrom_AddressRevert_DetectedAsChange()
		{
			var hop0_v1 = MakeSeries("1.1.1.1");
			var hop0_v2 = MakeSeries("2.2.2.2");
			var hop0_v3 = MakeSeries("1.1.1.1"); // same address as v1

			var snap1 = new RouteSnapshot(T0, new[] { hop0_v1 });
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new[] { hop0_v2 });
			var snap3 = new RouteSnapshot(T0.AddSeconds(2), new[] { hop0_v3 });

			// v1 -> v2: change
			Assert.Single(snap2.DiffFrom(snap1));
			// v2 -> v3 (back to 1.1.1.1): also a change
			Assert.Single(snap3.DiffFrom(snap2));
		}

		[Fact]
		public void DiffFrom_MultipleHops_OnlyChangedOnesReported()
		{
			var snap1 = new RouteSnapshot(T0, new HopTimeSeries[]
			{
				MakeSeries("1.1.1.1"),
				MakeSeries("2.2.2.2"),
				MakeSeries("3.3.3.3"),
			});
			var snap2 = new RouteSnapshot(T0.AddSeconds(1), new HopTimeSeries[]
			{
				MakeSeries("1.1.1.1"), // same
				MakeSeries("9.9.9.9"), // changed
				MakeSeries("3.3.3.3"), // same
			});

			var diff = snap2.DiffFrom(snap1);
			Assert.Single(diff);
			Assert.Equal(1, diff[0].HopIndex);
		}
	}
}

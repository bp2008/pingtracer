# Ping Tracer

Ping Tracer continuously pings each network host between your computer and a given destination, helping identify the source of connectivity problems.

![Ping Tracer](http://i.imgur.com/g5jmH0W.png)

This program helps to visually determine the origin of connection problems.  The latency over time is shown on graphs, and each instance of packet loss is marked in red.

A common use for such a tool is to monitor your connection to a multiplayer game server so you know who to blame when you experience lag.  For example, if you experience a terrible moment of lag and you see that every node beyond your router is showing elevated latency or packet loss, then the lag was on your end.  Typically, a poorly performing node will affect your connection to every node after it.

I built this program for personal use, and decided to share it for free as an open source project.  As such, it is light on features and polish.

Something you should be aware of is that when you attempt to "Graph every node leading to the destination", a trace route operation is performed in order to discover the hosts that will be monitored.

* The trace route operation is not optimized for speed, and will take many seconds to complete in most cases.
* The trace route operation attempts to contact each host (a.k.a. network node) only once.  Any host that fails to respond during the trace route operation will not be monitored.
* The trace route operation is ended if 5 consecutive hosts fail to respond.  This usually indicates that the destination host was already passed by and did not respond to the trace ping.
* Some hosts respond to the traceroute but do not respond to direct pings.  Technically it would still be possible to monitor these hosts by repeating their part of the traceroute, but I assume this would be against the wishes of the owner of the host.  If the owner wanted their router to be pingable, they would have enabled pinging, no?
* I let you increase the ping rate as high as 10 pings per second (per host!) which can add up to dozens or even hundreds of pings per second.  This is kind of, sort of, maybe a little bit excessive.  I don't recommend actually running it that high.  In fact 1 ping per second is probably all you need.







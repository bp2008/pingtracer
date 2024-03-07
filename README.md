# Ping Tracer

Ping Tracer continuously pings each network host between your computer and a given destination, helping identify the source of connectivity problems.

![image](https://github.com/bp2008/pingtracer/assets/5639911/3eff5b0c-b025-49bb-a149-77b2cb4c55ae)

This program helps to visually determine the origin of connection problems.  The latency over time is shown on graphs, and each instance of packet loss is marked in red.

A common use for such a tool is to monitor your connection to a multiplayer game server so you know who to blame when you experience lag.  For example, if you experience a terrible moment of lag and you see that every node beyond your router is showing elevated latency or packet loss, then the lag was on your end.  Typically, a poorly performing node will affect your connection to every node after it.

I built this program for personal use, and decided to share it for free as an open source project.  As such, it is light on features and polish.

Notes about PingTracer's "Trace Route" implementation:

* The trace route operation is not optimized for speed, and will take many seconds to complete if any hosts are unresponsive.
* The trace route operation attempts to contact each host (a.k.a. network node) only once.  Any host that fails to respond during the trace route operation will not be monitored.
* The trace route operation is ended if 5 consecutive hosts fail to respond.  This usually indicates that the destination host was already passed by and did not respond to the trace ping.
* Some hosts respond to the traceroute but do not respond to direct pings.  Such hosts are removed from monitoring after several seconds.
* You can increase the ping rate as high as 10 pings per second (per host!) which can add up to dozens or even hundreds of pings per second.  Please use responsibly.
* Some routers implement [ICMP rate limiting](https://docs.paloaltonetworks.com/pan-os/10-0/pan-os-admin/networking/session-settings-and-timeouts/icmp/icmpv6-rate-limiting.html) in such a way that penalizes rapid pinging.  Therefore, with higher ping rates you may see increased packet loss which is not representative of actual network performance.

## Installation

Just download the latest release from [the releases tab](https://github.com/bp2008/pingtracer/releases) and extract it wherever you like.

## Keyboard shortcuts for the graphs
Key | Alternate Key | action
-:|-:|-
Home | 9 | jump to beginning (first ping)
End | 0 | jump to end (last ping)
Page Up/Down | -/+ | move one page width to the left/right



## Command-Line arguments
```
Arguments:
    -h <value>
        Load a saved configuration with Display Name or Host field matching <value>.
        If a matching configuration is not found, one will be created.
    -4
        (Use with -h) This indicates the "Prefer IPv4" checkbox must be checked.
    -6
        (Use with -h) This indicates the "Prefer IPv4" checkbox must be unchecked.
    -t0
        (Use with -h) This indicates the "Trace Route" checkbox must be unchecked.
    -t1
        (Use with -h) This indicates the "Trace Route" checkbox must be checked.
    -l x,y,w,h
        The window will be moved to the specified location and size.
        "w" and "h" parameters are optional.
    -s
        Pinging will begin automatically.
    -m
        The ping graphs will be maximized.
```

See the latest command line arguments in-app with a usage example via Ping Tracer's `Tools` > `Command Line Arguments`.


## Building from source

PingTracer (as of version 1.17) is a relatively simple C# and Windows Forms app with no third-party dependencies except .NET Framework 4.6.2.  You should have no trouble building it in a standard installation of Visual Studio Community Edition.

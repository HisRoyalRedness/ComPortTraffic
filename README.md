# ComPortTraffic

[![Build Status][BS img]][Build Status]  [![Release Link][Release img]][Release Link]

Generate traffic to a COM port

## Usage

```txt
ComPortTraffic, version: 1.0.1

   Usage: ComPortTraffic [com=]<com> [baud=<baud>] [data=<data>]
                               [repeat=<repeat>] [gap=<gap>]

      where:
         repeat:      The COM port to connect to, eg. COM1.
         baud:        The baud rate. Default is 1000000.
         data:        The data to send, in hex, eg. 01020E0F
         repeat:      The number of times to repeat the transmission (-1 for infinite)
         gap:         The number of milliseconds delay between repetitions. Default is 0ms
```

For example,

```ComPortTraffic com4 baud=11520 data=303132ff repeat=4 gap=1000```

[Build Status]: https://ci.appveyor.com/project/KeithFletcher/comporttraffic
[BS img]: https://ci.appveyor.com/api/projects/status/466vvdp5ap8u5l47?svg=true

[Release Link]: https://github.com/HisRoyalRedness/ComPortTraffic/releases/latest
[Release img]: https://img.shields.io/github/v/release/HisRoyalRedness/ComPortTraffic
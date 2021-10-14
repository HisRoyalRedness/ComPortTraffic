# ComPortTraffic

[![Build Status][BS img]][Build Status]  [![Release Link][Release img]][Release Link]

Generate traffic to a COM port

## Usage

```txt
ComPortTraffic, version: 1.0.11

   Usage: ComPortTraffic [com=]<com> [baud=<baud>] [data=<data>] [len=<len>[,<len>]]
                               [file=<file>] [repeat=<repeat>] [gap=<gap[,gap]>]
                               [show]

      where:
         repeat:      The COM port to connect to, eg. COM1.
         baud:        The baud rate. Default is 1000000.
         data:        The data to send, in hex, eg. 01020E0F
         len:         The data length. It will truncate data is smaller than the length
                      of data. If it is longer, data will be padded with random bytes.
         file:        Path to a file to load binary data from.
                      The <file> argument overrides <data>.
         repeat:      The number of times to repeat the transmission (-1 for infinite)
         gap:         The number of milliseconds delay between repetitions. Default is 1s
         show:        If present, prints the data at each transmission

    Some arguments optionally allow a range to be specified (eg. 1,3). When a range is given,
    a random value within that range (inclusive) is chosen.
```

For example,

```ComPortTraffic com4 baud=11520 data=303132ff repeat=4 gap=1.2s,1.8s len=10,20```

[Build Status]: https://ci.appveyor.com/project/KeithFletcher/comporttraffic
[BS img]: https://ci.appveyor.com/api/projects/status/466vvdp5ap8u5l47?svg=true

[Release Link]: https://github.com/HisRoyalRedness/ComPortTraffic/releases/latest
[Release img]: https://img.shields.io/github/v/release/HisRoyalRedness/ComPortTraffic
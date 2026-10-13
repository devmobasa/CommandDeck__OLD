import Quickshell

ShellRoot {
  Counter { number: 1 }
  Counter { number: 2; visible: Number(Quickshell.env("CD_PROBE_COUNT")) > 1 }
  Counter { number: 3; visible: Number(Quickshell.env("CD_PROBE_COUNT")) > 2 }
}

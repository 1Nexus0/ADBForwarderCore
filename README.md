# ADBForwarder
Fully reengineered, ported to dotnet 8.
ADB library changed to AdvancedSharpAdbClient

Console application designed to handle forwarding TCP Ports (using [ADB](https://developer.android.com/studio/command-line/adb)) between your PC and Quest/Go HMDs, over USB

Specifically made for use with [ALVR](https://github.com/alvr-org/ALVR), for now. Supports the Oculus Go, Quest 1 and 2, Pico4, potentially any Android based devices

## Usage

* Download the latest release.
* Extract the archive somewhere convenient
* Run the program and ALVR, order does not matter
* ALVR may (or may not) restart
* You should see your device's serial ID show up in the console, if it says the following, all is well(device can be configured in appsetting.json)!
    * `Successfully forwarded device: 1WMHHXXXXXXXXX [hollywood]`
    * "hollywood" is Quest 2, "monterey" is Quest 1, "pacific" is Go, Phoenix_ovs is Pico4



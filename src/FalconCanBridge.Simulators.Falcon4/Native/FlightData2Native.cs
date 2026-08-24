using System.Runtime.InteropServices;

namespace FalconCanBridge.Simulators.Falcon4.Native;

/// <summary>
/// Byte-for-byte mirror of lightningstools/F4SharedMem's <c>FlightData2</c> struct (see
/// src/lightningstools/F4SharedMem/Headers/FlightData2.cs in this repo) - the secondary
/// "FalconSharedMemoryArea2" shared-memory block BMS publishes alongside the primary one, added
/// over several BMS4 versions (nozzle/rpm/FTIT/oil pressure "2" channels, altimeter/power/blink
/// bits, BMS version info, ECM, IFF transponder, ...).
///
/// See the remarks on <see cref="BMS4FlightDataNative"/> - same rationale: this struct exists only
/// so <see cref="Marshal.OffsetOf{T}(string)"/> computes correct offsets for us, never to read
/// memory directly. <c>FlightData2</c> uses <c>Pack = 8</c> upstream (unlike the primary struct's
/// <c>Pack = 1</c>), which means several fields have compiler-inserted padding before them - this
/// is precisely the kind of layout that is unsafe to hand-compute and is instead left entirely to
/// the CLR here.
///
/// Enum-typed upstream fields (<c>navMode</c>, <c>ecmOper</c>, <c>floodConsole</c>,
/// <c>RWRjammingStatus</c> elements, ...) are declared here using their underlying primitive type
/// (<c>byte</c>) instead of the actual enum type - identical size/alignment/offset behavior for
/// layout purposes, without needing to vendor every small state enum. The bitmask ([Flags]) enums
/// actually consumed by <see cref="Falcon4NativeFieldMapBuilder"/> to generate individual named
/// signals (AltBits, PowerBits, BlinkBits, BettyBits, MiscBits) are vendored separately in
/// BitFlagEnums.cs.
///
/// The EWMU/EWPI fields upstream are compiled out unless EWMU_AND_EWPI_PATCH_APPLIED is defined
/// (it isn't, here or upstream by default), so they are omitted here too - keeps the layout of
/// every field before them correct either way since they're the very last fields in the struct.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct FlightData2Native
{
    public const int RwrInfoSize = 512;
    public const int CallsignLen = 12;
    public const int MaxCallsigns = 32;
    public const int MaxEcmPrograms = 5;
    public const int TacanSourcesCount = 2;    // TacanSources.NUMBER_OF_SOURCES upstream
    public const int RttAreaWords = 28;        // RTT_areas.RTT_noOfAreas (7, without EWMU patch) * 4

    // VERSION 1
    public float nozzlePos2;   // Ownship engine nozzle2 percent open (0-100)
    public float rpm2;         // Ownship engine rpm2 (Percent 0-103)
    public float ftit2;        // Ownship Forward Turbine Inlet Temp2 (Degrees C)
    public float oilPressure2; // Ownship Oil Pressure2 (Percent 0-100)
    public byte navMode;       // current mode selected for HSI/eHSI
    public float aauz;         // Ownship barometric altitude given by AAU (depends on calibration)
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = TacanSourcesCount)]
    public byte[] tacanInfo;   // Tacan band/mode settings for UFC and AUX COMM

    // VERSION 2
    public int AltCalReading;  // barometric altitude calibration (depends on CalType)
    public uint altBits;       // various altimeter bits - see AltBits enum
    public uint powerBits;     // power bus / generator states - see PowerBits enum
    public uint blinkBits;     // indicator lights blink status - see BlinkBits enum
    public int cmdsMode;       // CMDS mode state - see CmdsModes enum
    public int BupUhfPreset;   // BUP UHF channel preset

    // VERSION 3
    public int BupUhfFreq;     // BUP UHF channel frequency
    public float cabinAlt;     // Ownship cabin altitude
    public float hydPressureA; // Ownship Hydraulic Pressure A
    public float hydPressureB; // Ownship Hydraulic Pressure B
    public uint currentTime;   // Current time in seconds (max 60*60*24)
    public short vehicleACD;   // Ownship ACD index number (aircraft type)
    public int VersionNum2;    // Version of FlightData2 mem area

    // VERSION 4
    public float fuelFlow2;    // Ownship fuel flow2 (Lbs/Hour)

    // VERSION 5 / 8
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = RwrInfoSize)]
    public byte[] RwrInfo;     // New RWR Info (undecoded raw block)
    public float lefPos;       // Ownship LEF position
    public float tefPos;       // Ownship TEF position

    // VERSION 6
    public float vtolPos;      // Ownship VTOL exhaust angle

    // VERSION 9
    public byte pilotsOnline;  // Number of pilots in an MP session
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCallsigns * CallsignLen)]
    public byte[] pilotsCallsign; // MAX_CALLSIGNS * CALLSIGN_LEN raw bytes (flattened Callsign_LineOfText[])
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxCallsigns)]
    public byte[] pilotsStatus;   // Status of the MP pilots - see FlyStates enum

    // VERSION 10
    public float bumpIntensity; // Intensity of a "bump" while taxiing/rolling, 0..1

    // VERSION 11
    public float latitude;  // Ownship latitude in degrees (as known by avionics)
    public float longitude; // Ownship longitude in degrees (as known by avionics)

    // VERSION 12
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
    public ushort[] RTT_size; // RTT overall width and height
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = RttAreaWords)]
    public ushort[] RTT_area; // For each area: left/top/right/bottom

    // VERSION 13
    public byte iffBackupMode1Digit1;
    public byte iffBackupMode1Digit2;
    public byte iffBackupMode3ADigit1;
    public byte iffBackupMode3ADigit2;

    // VERSION 14
    public byte instrLight; // current instrument backlight brightness - see InstrLight enum

    // VERSION 15
    public uint bettyBits;   // see BettyBits enum
    public uint miscBits;    // see MiscBits enum
    public float RALT;       // radar altitude (only valid if MiscBits.RALT_Valid is set)
    public float bingoFuel;  // bingo fuel level
    public float caraAlow;   // cara alow setting
    public float bullseyeX;  // bullseye X in sim coordinates (North, Ft)
    public float bullseyeY;  // bullseye Y in sim coordinates (East, Ft)
    public int BMSVersionMajor;
    public int BMSVersionMinor;
    public int BMSVersionMicro;
    public int BMSBuildNumber;
    public uint StringAreaSize;
    public uint StringAreaTime;
    public uint DrawingAreaSize;

    // VERSION 16
    public float turnRate; // actual turn rate in degrees/second

    // VERSION 18
    public byte floodConsole; // current floodconsole brightness - see FloodConsole enum

    // VERSION 19
    public float magDeviationSystem;
    public float magDeviationReal;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxEcmPrograms)]
    public uint[] ecmBits; // see EcmBits enum - mutually exclusive states, not combinable
    public byte ecmOper;   // see EcmOperStates enum
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = BMS4FlightDataNative.MaxRwrObjects)]
    public byte[] RWRjammingStatus; // see JammingStates enum

    // VERSION 20
    public int radio2_preset;
    public int radio2_frequency;
    public sbyte iffTransponderActiveCode1; // mode 1, negative = OFF/n.a.
    public short iffTransponderActiveCode2; // mode 2
    public short iffTransponderActiveCode3A; // mode 3A
    public short iffTransponderActiveCodeC; // mode C
    public short iffTransponderActiveCode4; // mode 4

    // VERSION 21
    public int tacan_ils_frequency; // Tacan ILS (110.30 = 11030)

    // VERSION 22
    public int desired_RTT_FPS;

    public float sideSlipdeg; // ADI side slip

    // VERSION 23
    public float gsMax;
    public float gsMin;
}

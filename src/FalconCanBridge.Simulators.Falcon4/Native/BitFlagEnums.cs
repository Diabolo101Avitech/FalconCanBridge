using System;

namespace FalconCanBridge.Simulators.Falcon4.Native;

// Vendored copies of the [Flags] bitmask enums from lightningstools/F4SharedMem/Headers that
// describe the individual named bits inside BMS4FlightDataNative.lightBits/lightBits2/lightBits3/
// hsiBits and FlightData2Native.altBits/powerBits/blinkBits/bettyBits/miscBits.
//
// Falcon4NativeFieldMapBuilder reflects over these (Enum.GetValues) to generate one
// Falcon4FieldDefinition (CanDataType.Bit) per single-bit member automatically - composite
// "AllXxxOn"-style aggregate members (not a single bit) are filtered out because they aren't a
// power of two. Keeping the real upstream member names means the generated signal names
// (e.g. "lightBits_MasterCaution") are directly traceable back to F4SharedMem's own headers.

[Flags]
internal enum LightBitsNative : uint
{
    MasterCaution = 0x1,
    TF = 0x2,
    OXY_BROW = 0x4,
    EQUIP_HOT = 0x8,
    ONGROUND = 0x10,
    ENG_FIRE = 0x20,
    CONFIG = 0x40,
    HYD = 0x80,
    Flcs_ABCD = 0x100,
    FLCS = 0x200,
    CAN = 0x400,
    T_L_CFG = 0x800,
    AOAAbove = 0x1000,
    AOAOn = 0x2000,
    AOABelow = 0x4000,
    RefuelRDY = 0x8000,
    RefuelAR = 0x10000,
    RefuelDSC = 0x20000,
    FltControlSys = 0x40000,
    LEFlaps = 0x80000,
    EngineFault = 0x100000,
    Overheat = 0x200000,
    FuelLow = 0x400000,
    Avionics = 0x800000,
    RadarAlt = 0x1000000,
    IFF = 0x2000000,
    ECM = 0x4000000,
    Hook = 0x8000000,
    NWSFail = 0x10000000,
    CabinPress = 0x20000000,
    AutoPilotOn = 0x40000000,
    TFR_STBY = 0x80000000,
    // AllLampBitsOn (composite) intentionally omitted from generation.
}

[Flags]
internal enum LightBits2Native : uint
{
    HandOff = 0x1,
    Launch = 0x2,
    PriMode = 0x4,
    Naval = 0x8,
    Unk = 0x10,
    TgtSep = 0x20,
    Go = 0x40,
    NoGo = 0x80,
    Degr = 0x100,
    Rdy = 0x200,
    ChaffLo = 0x400,
    FlareLo = 0x800,
    AuxSrch = 0x1000,
    AuxAct = 0x2000,
    AuxLow = 0x4000,
    AuxPwr = 0x8000,
    EcmPwr = 0x10000,
    EcmFail = 0x20000,
    FwdFuelLow = 0x40000,
    AftFuelLow = 0x80000,
    EPUOn = 0x100000,
    JFSOn = 0x200000,
    SEC = 0x400000,
    OXY_LOW = 0x800000,
    PROBEHEAT = 0x1000000,
    SEAT_ARM = 0x2000000,
    BUC = 0x4000000,
    FUEL_OIL_HOT = 0x8000000,
    ANTI_SKID = 0x10000000,
    TFR_ENGAGED = 0x20000000,
    GEARHANDLE = 0x40000000,
    ENGINE = 0x80000000,
}

[Flags]
internal enum LightBits3Native : uint
{
    FlcsPmg = 0x1,
    MainGen = 0x2,
    StbyGen = 0x4,
    EpuGen = 0x8,
    EpuPmg = 0x10,
    ToFlcs = 0x20,
    FlcsRly = 0x40,
    BatFail = 0x80,
    Hydrazine = 0x100,
    Air = 0x200,
    Elec_Fault = 0x400,
    Lef_Fault = 0x800,
    OnGround = 0x1000,
    FlcsBitRun = 0x2000,
    FlcsBitFail = 0x4000,
    DbuWarn = 0x8000,
    NoseGearDown = 0x10000,
    LeftGearDown = 0x20000,
    RightGearDown = 0x40000,
    ParkBrakeOn = 0x100000,
    Power_Off = 0x200000,
    cadc = 0x400000,
    SpeedBrake = 0x800000,
    SysTest = 0x1000000,
    MCAnnounced = 0x2000000,
    MLGWOW = 0x4000000,
    NLGWOW = 0x8000000,
    ATF_Not_Engaged = 0x10000000,
    Inlet_Icing = 0x20000000,
}

[Flags]
internal enum HsiBitsNative : uint
{
    ToTrue = 0x01,
    IlsWarning = 0x02,
    CourseWarning = 0x04,
    Init = 0x08,
    TotalFlags = 0x10,
    ADI_OFF = 0x20,
    ADI_AUX = 0x40,
    ADI_GS = 0x80,
    ADI_LOC = 0x100,
    HSI_OFF = 0x200,
    BUP_ADI_OFF = 0x400,
    VVI = 0x800,
    AOA = 0x1000,
    AVTR = 0x2000,
    OuterMarker = 0x4000,
    MiddleMarker = 0x8000,
    FromTrue = 0x10000,
    Flying = 0x80000000,
}

[Flags]
internal enum AltBitsNative : uint
{
    CalType = 0x01,
    PneuFlag = 0x02,
}

[Flags]
internal enum PowerBitsNative : uint
{
    BusPowerBattery = 0x01,
    BusPowerEmergency = 0x02,
    BusPowerEssential = 0x04,
    BusPowerNonEssential = 0x08,
    MainGenerator = 0x10,
    StandbyGenerator = 0x20,
    JetFuelStarter = 0x40,
}

[Flags]
internal enum BlinkBitsNative : uint
{
    OuterMarker = 0x01,
    MiddleMarker = 0x02,
    PROBEHEAT = 0x04,
    AuxSrch = 0x08,
    Launch = 0x10,
    PriMode = 0x20,
    Unk = 0x40,
    Elec_Fault = 0x80,
    OXY_BROW = 0x100,
    EPUOn = 0x200,
    JFSOn_Slow = 0x400,
    JFSOn_Fast = 0x800,
    ECM_Oper = 0x1000,
}

[Flags]
internal enum BettyBitsNative : uint
{
    Betty_Allwords = 0x00001,
    Betty_Pullup = 0x00002,
    Betty_Altitude = 0x00004,
    Betty_Warning = 0x00008,
    Betty_Jammer = 0x00010,
    Betty_Counter = 0x00020,
    Betty_ChaffFlare = 0x00040,
    Betty_ChaffFlare_Low = 0x00080,
    Betty_ChaffFlare_Out = 0x00100,
    Betty_Lock = 0x00200,
    Betty_Caution = 0x00400,
    Betty_Bingo = 0x00800,
    Betty_Data = 0x01000,
    Betty_IFF = 0x02000,
    Betty_Lowspeed = 0x04000,
    Betty_Beeps = 0x08000,
    Betty_AOA = 0x10000,
    Betty_MaxG = 0x20000,
}

[Flags]
internal enum MiscBitsNative : uint
{
    RALT_Valid = 0x1,
    Flcs_Flcc_A = 0x02,
    Flcs_Flcc_B = 0x04,
    Flcs_Flcc_C = 0x08,
    Flcs_Flcc_D = 0x10,
    SolenoidStatus = 0x20,
    // AllLampBitsFlccOn (composite) intentionally omitted from generation.
}

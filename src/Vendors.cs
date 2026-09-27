// Names for common USB vendor IDs. Devices usually report their maker
// themselves; this fills the gap when they don't. Unknown IDs show as numbers.
using System.Collections.Generic;

static class Vendors
{
    static readonly Dictionary<int, string> names = new Dictionary<int, string>
    {
        { 0x03F0, "HP" }, { 0x0403, "FTDI" }, { 0x041E, "Creative" }, { 0x0424, "Microchip (SMSC)" },
        { 0x043E, "LG Electronics" }, { 0x045E, "Microsoft" }, { 0x046D, "Logitech" }, { 0x0480, "Toshiba" },
        { 0x04A9, "Canon" }, { 0x04B8, "Epson" }, { 0x04CA, "Lite-On" }, { 0x04D9, "Holtek" },
        { 0x04E8, "Samsung" }, { 0x04F2, "Chicony" }, { 0x04F9, "Brother" }, { 0x050D, "Belkin" },
        { 0x054C, "Sony" }, { 0x056A, "Wacom" }, { 0x057E, "Nintendo" }, { 0x058F, "Alcor Micro" },
        { 0x05AC, "Apple" }, { 0x05DC, "Lexar" }, { 0x05E3, "Genesys Logic" }, { 0x0634, "Micron" },
        { 0x067B, "Prolific" }, { 0x0781, "SanDisk" }, { 0x090C, "Silicon Motion" }, { 0x0930, "Toshiba" },
        { 0x0951, "Kingston" }, { 0x0A12, "Cambridge Silicon Radio" }, { 0x0A5C, "Broadcom" }, { 0x0B05, "ASUS" },
        { 0x0B95, "ASIX" }, { 0x0BB4, "HTC" }, { 0x0BC2, "Seagate" }, { 0x0BDA, "Realtek" },
        { 0x0C45, "Sonix (Microdia)" }, { 0x0CF3, "Qualcomm Atheros" }, { 0x0D8C, "C-Media" }, { 0x0E8D, "MediaTek" },
        { 0x0FD9, "Elgato" }, { 0x1004, "LG Electronics" }, { 0x1038, "SteelSeries" }, { 0x1050, "Yubico" },
        { 0x1058, "Western Digital" }, { 0x10C4, "Silicon Labs" }, { 0x12D1, "Huawei" }, { 0x138A, "Validity Sensors" },
        { 0x1395, "Sennheiser" }, { 0x13D3, "AzureWave" }, { 0x13FE, "Phison" }, { 0x1532, "Razer" },
        { 0x152D, "JMicron" }, { 0x154B, "PNY" }, { 0x174C, "ASMedia" }, { 0x17EF, "Lenovo" },
        { 0x18A5, "Verbatim" }, { 0x18D1, "Google" }, { 0x1A40, "Terminus Technology" }, { 0x1A86, "WCH (QinHeng)" },
        { 0x1B1C, "Corsair" }, { 0x1BCF, "Sunplus" }, { 0x2109, "VIA Labs" }, { 0x22B8, "Motorola" },
        { 0x2537, "Norelsys" }, { 0x258A, "Sinowealth" }, { 0x2717, "Xiaomi" }, { 0x28DE, "Valve" },
        { 0x2A70, "OnePlus" }, { 0x2C7C, "Quectel" }, { 0x2E8A, "Raspberry Pi" }, { 0x413C, "Dell" },
        { 0x8086, "Intel" }, { 0x8087, "Intel" }, { 0x8564, "Transcend" },
    };

    public static string Name(int vid)
    {
        string n;
        return names.TryGetValue(vid, out n) ? n : null;
    }
}

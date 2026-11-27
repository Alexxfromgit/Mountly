using Avalonia.Media;

namespace Mountly.App.Controls;

/// <summary>
/// Mountly's line icons on a 24-unit grid, drawn with a round 1.6-unit stroke (see the "icon" Path style).
/// </summary>
public static class Icons
{
    public static Geometry Disc { get; } = Geometry.Parse("M12,3 a9,9 0 1 0 0.01,0 Z M12,9.5 a2.5,2.5 0 1 0 0.01,0 Z");
    public static Geometry Library { get; } = Geometry.Parse("M3,7.5 L12,3 L21,7.5 L12,12 Z M3,12 L12,16.5 L21,12 M3,16.5 L12,21 L21,16.5");
    public static Geometry Tools { get; } = Geometry.Parse("M4,6 H13 M17,6 H20 M15,4 V8 M4,12 H7 M11,12 H20 M9,10 V14 M4,18 H15 M19,18 H20 M17,16 V20");
    public static Geometry Settings { get; } = Geometry.Parse(
        "M12,8.5 a3.5,3.5 0 1 0 0.01,0 Z M12,2.5 L13.6,5 L16.5,4.2 L17,7.1 L19.8,8 L18.9,10.8 L21,13 L18.8,14.8 L19.4,17.7 L16.5,18 L15.4,20.8 L12.8,19.5 L10.2,21 L9,18.3 L6,18.3 L6,15.3 L3.4,13.9 L4.8,11.3 L3.6,8.6 L6.4,7.6 L6.8,4.6 L9.8,5.1 Z");
    public static Geometry CreateIso { get; } = Geometry.Parse("M3,7 a2,2 0 0 1 2,-2 h4 l2,2 h8 a2,2 0 0 1 2,2 v8 a2,2 0 0 1 -2,2 H5 a2,2 0 0 1 -2,-2 Z M12,10 v6 M9,13 h6");
    public static Geometry Convert { get; } = Geometry.Parse("M4,8 H17 M13,4 L17,8 L13,12 M20,16 H7 M11,12 L7,16 L11,20");
    public static Geometry Browse { get; } = Geometry.Parse("M10.5,4 a6.5,6.5 0 1 0 0.01,0 Z M15.5,15.5 L20,20");
    public static Geometry Verify { get; } = Geometry.Parse("M12,3 L19,6 V11 C19,15.5 16,19 12,21 C8,19 5,15.5 5,11 V6 Z M9,12 L11,14 L15,10");
    public static Geometry Rip { get; } = Geometry.Parse("M12,3 a9,9 0 1 0 0.01,0 Z M12,7.5 V15.5 M8.5,12 L12,15.5 L15.5,12");
    public static Geometry Eject { get; } = Geometry.Parse("M12,5 L19,13 H5 Z M5,18 H19");
    public static Geometry Folder { get; } = Geometry.Parse("M3,7 a2,2 0 0 1 2,-2 h4 l2,2 h8 a2,2 0 0 1 2,2 v8 a2,2 0 0 1 -2,2 H5 a2,2 0 0 1 -2,-2 Z");
    public static Geometry File { get; } = Geometry.Parse("M6,3 H14 L19,8 V21 H6 Z M14,3 V8 H19");
    public static Geometry Plus { get; } = Geometry.Parse("M12,5 V19 M5,12 H19");
    public static Geometry More { get; } = Geometry.Parse("M4,12 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 Z M10.7,12 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 Z M17.4,12 a1.3,1.3 0 1 0 2.6,0 a1.3,1.3 0 1 0 -2.6,0 Z");
    public static Geometry Up { get; } = Geometry.Parse("M12,19 V5 M6,11 L12,5 L18,11");
    public static Geometry Pin { get; } = Geometry.Parse("M9,3 H15 M10,3 V9 L6,13 H18 L14,9 V3 M12,13 V21");
    public static Geometry Close { get; } = Geometry.Parse("M6,6 L18,18 M18,6 L6,18");
    public static Geometry Check { get; } = Geometry.Parse("M5,12.5 L10,17.5 L19,7");
    public static Geometry Alert { get; } = Geometry.Parse("M12,3 L22,20 H2 Z M12,9 V14 M12,16.8 V17.2");
}

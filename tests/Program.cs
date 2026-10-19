using BelmontsCurseGraphics;

var count = 0;
void Check(bool result, string name)
{
    if (!result) throw new Exception(name);
    count++;
}
void Reject(float sharpness)
{
    try { new Fsr4Options(Sharpness: sharpness).Validate(); }
    catch (ArgumentException) { count++; return; }
    throw new Exception("Invalid sharpness accepted: " + sharpness);
}
var defaults = new Fsr4Options();
Check(!defaults.Enabled && defaults.Sharpness == .2f, "Fresh installations retain native AA until FSR is selected");
new Fsr4Options(true, 0).Validate(); new Fsr4Options(true, 1).Validate();
foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 1.1f }) Reject(invalid);

// The mod appends a menu index; it never substitutes that index into the game's saved enum.
var native = new[] { 0, 2, 5 };
Check(Fsr4MenuModel.DisplayIndex(true, 2, native) == 3, "FSR selection occupies the appended index");
Check(Fsr4MenuModel.DisplayIndex(false, 2, native) == 1, "Returning to native AA follows the game's value mapping");
Check(Fsr4MenuModel.DisplayIndex(false, 5, native) == 2, "Native enum values need not be consecutive");
Check(Fsr4MenuModel.IsFsrChoice(3, native.Length) && !Fsr4MenuModel.IsNativeChoice(3, native.Length), "FSR choice is intercepted before the game's setter");
for (var i = 0; i < native.Length; ++i)
    Check(Fsr4MenuModel.IsNativeChoice(i, native.Length) && !Fsr4MenuModel.IsFsrChoice(i, native.Length), "Original AA choices reach the original binding");
Check(native.SequenceEqual(new[] { 0, 2, 5 }), "Native saved enum mapping is unchanged");
Check(Fsr4MenuModel.IsFsrChoice(4, 4) && Fsr4MenuModel.IsNativeChoice(3, 4), "Additional native AA modes are preserved");
Check(!Fsr4MenuModel.IsNativeChoice(-1, 3) && !Fsr4MenuModel.IsFsrChoice(4, 3), "Invalid menu indices are rejected");
Check(Fsr4MenuModel.DisplayIndex(false, 99, native) == 0, "Unknown native display values have a safe selection");
Check(Fsr4MenuModel.SharpnessIndex(0) == 0 && Fsr4MenuModel.SharpnessIndex(1) == 10 && Fsr4MenuModel.SharpnessIndex(.2f) == 2,
    "Native sharpening options represent Off, default 20 percent and maximum");

var supported = new Fsr4Requirements(true, true, true, true, false, false, false);
Check(supported.Failure == null, "Supported LDR world camera can run FSR");
foreach (var incompatible in new[] {
    supported with { Backend = false }, supported with { PostProcessing = false },
    supported with { TemporalShader = false }, supported with { MotionVectors = false },
    supported with { Stacked = true }, supported with { DynamicResolution = true }, supported with { Hdr = true } })
    Check(!string.IsNullOrEmpty(incompatible.Failure), "Unsupported FSR inputs restore game AA with a reason");

var jitter = Fsr4Coordinates.JitterPixels(1f / 5120, 1f / 2160, 5120, 2160, true);
Check(Math.Abs(jitter.X - .5f) < .0001f && Math.Abs(jitter.Y + .5f) < .0001f, "DX11 clip jitter is converted to texture pixels with inverted Y");
Check(Fsr4Coordinates.JitterPixels(0, 0, 5120, 2160, true) == (0f, 0f), "An unjittered projection supplies zero offsets");
Console.WriteLine($"FSR Native AA: {count} checks passed.");

# Banner finish — 5 November 2026

Owner requests graphical finishing from two supplied references, preserving
colors, emphasizing depth-of-field fade and the MECA gear. Scope is the header,
not the mockup's historical navigation/diagnostics/RPM features.

Generated a new monochrome steel gear macro using the built-in image_gen tool.
Project asset: src/Assets/meca-gear-banner-20261105.png (embedded WPF Resource).
References guide mood/composition; no user screenshot is bundled or overwritten.
The image is illustrative header artwork, not an attested photograph of the
owner's exact pedal hardware. Original generated output is preserved separately.

The 112px header uses existing graphite/silver colors and a soft left/bottom
fade, with crisp live MECA / Calibration Wizard text. No new palette, menus,
commands, telemetry or calibration behavior. All named controls/event bindings,
white-faced gauges/blue needles and C# product source remain unchanged.

Validation: actual WPF App/MainWindow content rendered offscreen at default
1040x800 and minimum880x740 layouts using synthetic two-/three-pedal data. No
Show/Loaded/Run, hardware connection or settings write. Banner asset loaded and
text/badge stayed readable. Existing body scroll behavior at compact sizes is
retained; this is not a claim all three instruments fit without scrolling.
Render harness and preview PNGs in ../tmp/meca-banner-finish-20261105, including
banner-preview.png. An initial harness crop included root margin in its bitmap;
corrected harness removes that margin for output and paints the app background.
Final header kept compact after initial154px experiment. No extra native/device
or unchanged functional test suite repeated for this visual-only change.
Windows build 0warnings/0errors; self-contained publish clean (publish.log).
Private ZIP entry hashes verified; exact package receipt beside the ZIP.

## Final generation prompt (built-in image_gen, no CLI/API fallback)

Use case: product-mockup. Create one production background asset for the header
banner of the existing MECA pedal calibration Windows application, inspired by
the user's two reference screenshots (not a whole UI mockup). Wide cinematic
3:1 landscape. Monochrome black/graphite and brushed silver steel ONLY, retaining
the application's existing neutral dark palette. The essential subject is an
extreme macro photograph of a precision-machined MECA-style steel gear/cog,
large curved rim with several broad robust teeth, oriented diagonally across
the right two thirds. Sharp brushed machining and silver edge highlights on
the nearest central-right teeth; very shallow depth of field, beautifully soft
optical bokeh, blurred far teeth and background. Important: left45% is nearly
black graphite negative space for real application text to be overlaid later.
Soft natural darkness/blur fade toward left and along bottom edge, no hard
photo boundary. Refined premium industrial motorsport hardware photography,
understated, realistic steel, not a fantasy gear drawing. Keep visual interest
within the middle horizontal third so header cropping remains attractive. No
text, no letters, no logos drawn into image, no badges, no controls, no user
interface, no extra colors, no watermark. The real MECA title will be added as
crisp vector text in the app.

# Internal Stream Validation

Stream Validation records raw packets and applied poses for MOVIN development. It is not needed
for motion, point clouds, Studio status replies or received FPS.

The full repository retains `MOVINStreamReceiver.Validation.cs` and its tests behind the
`MOVIN_STREAM_VALIDATION` scripting define. It is off by default, including in the Unity Editor.
For internal testing, open the repository project and add this symbol in Player Settings > Other
Settings > Scripting Define Symbols. The internal tests then compile and run with the receiver tests.
An isolated batch-test project can set the same define using `Assets/csc.rsp`.

The release exporter explicitly excludes the Validation source and metadata. Do not turn this
symbol on in a user installation; use the complete source checkout for internal diagnostics.
Without the symbol there are no validation fields, log writers, raw-packet capture, validation
control queue or Validation/Session/Log monitor rows.

Internal Begin/End controls use `/MOVIN/StreamValidation/Begin` and `/MOVIN/StreamValidation/End`.
Validation wire frames use `-(index + 1)`. Logs default to Documents/MOVIN Studio/StreamValidation/Unity;
configure a local output directory when necessary. Network-supplied output paths are ignored.
Session names accept 1–64 ASCII letters, digits, underscores and hyphens; existing files are not overwritten.
Collect plugin logs on the receiver machine when testing across computers.

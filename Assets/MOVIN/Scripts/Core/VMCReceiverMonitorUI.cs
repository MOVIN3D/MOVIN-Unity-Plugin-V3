using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[AddComponentMenu("MOVIN/VMC Receiver Monitor UI")]
[DisallowMultipleComponent]
public class VMCReceiverMonitorUI : MonoBehaviour
{
    private const int MinimumPanelWidth = 320;
    private const int RowLabelWidth = 220;
    private const float MinPanelHeightRatio = 0.25f;
    private const float MaxPanelHeightRatio = 1f;
    private const float PanelVerticalPadding = 28f;
    private const float HeaderHeightRatio = 0.06f;
    private const float DebugHeaderHeightRatio = 0.04f;
    private const float DividerHeightRatio = 0.01f;
    private const float RowFontHeightRatio = 0.48f;
    private const float HeaderMargin = 8f;
    private const float RowVerticalMargin = 4f;

    private static readonly Color PanelBackgroundColor = new Color(0.05f, 0.06f, 0.07f, 0.9f);
    private static readonly Color PanelBorderColor = new Color(0.25f, 0.28f, 0.3f, 1f);
    private static readonly Color DividerColor = new Color(0.25f, 0.28f, 0.3f, 0.9f);
    private static readonly Color RowLabelColor = new Color(0.76f, 0.82f, 0.87f, 1f);
    private static readonly Color ValueColor = Color.white;

    [Header("Source")]
    public VMCReceiver receiver;

    [Header("Display")]
    public bool visible = true;
    public bool createUIDocumentIfMissing = true;
    public bool showDebugDetails = false;
    public float refreshInterval = 0.2f;
    public int panelWidth = 460;
    [Range(0.25f, 1f)]
    public float panelHeightScreenRatio = 2f / 3f;

    private UIDocument _document;
    private bool _createdDocument;
    private PanelSettings _createdPanelSettings;
    private VisualElement _panel;
    private Label _status;
    private Label _port;
    private Label _socketFps;
    private Label _mainFps;
    private Label _inputFps;
    private Label _appliedFps;
    private Label _frame;
    private Label _dropped;
    private Label _packetAge;
    private Label _playbackLatency;
    private Label _buffer;
    private Label _queue;
    private Label _processingErrors;
    private Label _validation;
    private Label _session;
    private Label _packets;
    private Label _messages;
    private Label _dispatch;
    private Label _messageRate;
    private Label _lastAddress;
    private Label _lastPose;
    private Label _background;
    private Label _logPath;
    private readonly List<VisualElement> _rows = new List<VisualElement>();
    private readonly List<Label> _rowLabels = new List<Label>();
    private readonly List<VisualElement> _dividers = new List<VisualElement>();
    private VisualElement _header;
    private Label _title;
    private Label _debugTitle;

    private float _nextRefreshTime;
    private float _lastRateSampleTime;
    private long _lastSocketTickCount;
    private long _lastMainThreadFrameCount;
    private long _lastInputFrameCount;
    private long _lastAppliedFrameCount;
    private long _lastDispatchedCount;
    private float _socketTickRate;
    private float _mainThreadFrameRate;
    private float _inputFrameRate;
    private float _appliedFrameRate;
    private float _messageDispatchRate;
    private bool _hasRateSample;
    private bool _builtDebugDetails;
    private static Font _monitorFont;

    private void Reset()
    {
        receiver = GetComponent<VMCReceiver>();
    }

    private void OnEnable()
    {
        if (!receiver)
            receiver = GetComponent<VMCReceiver>();

        EnsureDocument();
        BuildPanel();
    }

    private void OnDisable()
    {
        _panel?.RemoveFromHierarchy();
        _panel = null;

        if (_createdPanelSettings)
        {
            Destroy(_createdPanelSettings);
            _createdPanelSettings = null;
        }

        if (_createdDocument && _document)
        {
            Destroy(_document);
            _document = null;
        }

        _createdDocument = false;
    }

    private void Update()
    {
        if (!visible)
        {
            if (_panel != null)
                _panel.style.display = DisplayStyle.None;
            return;
        }

        if (_panel == null || !IsPanelReady() || _builtDebugDetails != showDebugDetails)
        {
            _panel?.RemoveFromHierarchy();
            _panel = null;
            EnsureDocument();
            BuildPanel();
        }

        if (_panel == null)
            return;

        _panel.style.display = DisplayStyle.Flex;
        ApplyPanelSize();
        ApplyPanelLayout();

        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
        Refresh();
        ApplyPanelLayout();
    }

    private void EnsureDocument()
    {
        if (_document)
            return;

        _document = GetComponent<UIDocument>();
        if (!_document && createUIDocumentIfMissing)
        {
            _document = gameObject.AddComponent<UIDocument>();
            _document.enabled = false;
            _createdDocument = true;
        }

        if (!_document)
            return;

        if (!_document.panelSettings)
        {
            _createdPanelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _createdPanelSettings.name = "MOVIN Receiver Monitor Panel";
            _createdPanelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            _createdPanelSettings.sortingOrder = 100;
            _document.panelSettings = _createdPanelSettings;
        }

        if (!_document.enabled)
            _document.enabled = true;
    }

    private void BuildPanel()
    {
        if (!_document || _document.rootVisualElement == null || _panel != null)
            return;

        ResetRateSamples();
        _rows.Clear();
        _rowLabels.Clear();
        _dividers.Clear();
        _header = null;
        _title = null;
        _debugTitle = null;

        _document.rootVisualElement.style.position = Position.Absolute;
        _document.rootVisualElement.style.left = 0;
        _document.rootVisualElement.style.top = 0;
        _document.rootVisualElement.style.right = 0;
        _document.rootVisualElement.style.bottom = 0;

        _panel = new VisualElement { name = "movin-receiver-monitor" };
        _panel.style.position = Position.Absolute;
        _panel.style.left = 12;
        _panel.style.top = 12;
        ApplyPanelSize();
        _panel.style.paddingLeft = 16;
        _panel.style.paddingRight = 16;
        _panel.style.paddingTop = 14;
        _panel.style.paddingBottom = 14;
        _panel.style.backgroundColor = PanelBackgroundColor;
        _panel.style.borderTopLeftRadius = 6;
        _panel.style.borderTopRightRadius = 6;
        _panel.style.borderBottomLeftRadius = 6;
        _panel.style.borderBottomRightRadius = 6;
        _panel.style.borderLeftWidth = 1;
        _panel.style.borderRightWidth = 1;
        _panel.style.borderTopWidth = 1;
        _panel.style.borderBottomWidth = 1;
        _panel.style.borderLeftColor = PanelBorderColor;
        _panel.style.borderRightColor = PanelBorderColor;
        _panel.style.borderTopColor = PanelBorderColor;
        _panel.style.borderBottomColor = PanelBorderColor;

        _header = new VisualElement();
        _header.style.flexDirection = FlexDirection.Row;
        _header.style.alignItems = Align.Center;
        _header.style.justifyContent = Justify.SpaceBetween;
        _header.style.marginBottom = 8;
        _header.style.flexShrink = 0;
        _panel.Add(_header);

        _title = new Label("MOVIN Receiver");
        ApplyLabelStyle(_title, Color.white, 18, TextAnchor.MiddleLeft, true);
        _title.style.height = 30;
        _header.Add(_title);

        _status = new Label("Starting");
        _status.style.paddingLeft = 8;
        _status.style.paddingRight = 8;
        _status.style.paddingTop = 3;
        _status.style.paddingBottom = 3;
        _status.style.borderTopLeftRadius = 4;
        _status.style.borderTopRightRadius = 4;
        _status.style.borderBottomLeftRadius = 4;
        _status.style.borderBottomRightRadius = 4;
        ApplyLabelStyle(_status, Color.white, 13, TextAnchor.MiddleCenter, true);
        _status.style.height = 30;
        _header.Add(_status);

        BuildRuntimeRows();
        if (showDebugDetails)
            BuildDebugRows();

        _document.rootVisualElement.Add(_panel);
        _builtDebugDetails = showDebugDetails;
        Refresh();
        ApplyPanelLayout();
    }

    private void BuildRuntimeRows()
    {
        _packetAge = AddRow("Packet Age");
        _frame = AddRow("Frame");
        _port = AddRow("Port");
        AddSectionDivider();
        _socketFps = AddRow("Socket FPS");
        _inputFps = AddRow("Input FPS");
        _appliedFps = AddRow("Applied Frame FPS");
        _mainFps = AddRow("Main Tick FPS");
        AddSectionDivider();
        _buffer = AddRow("Pose Buffer");
        _playbackLatency = AddRow("Playback Latency");
        _dropped = AddRow("Dropped");
        AddSectionDivider();
        _queue = AddRow("Main Msg Queue");
        _processingErrors = AddRow("Processing Error");
        AddSectionDivider();
        _validation = AddRow("Validation");
        _session = AddRow("Session");
    }

    private void BuildDebugRows()
    {
        AddDebugTitle();
        _packets = AddRow("Packets");
        _messages = AddRow("Messages");
        _dispatch = AddRow("Dispatched");
        _messageRate = AddRow("Message rate");
        _lastAddress = AddRow("Last OSC");
        _lastPose = AddRow("Last pose");
        _background = AddRow("Background");
        _logPath = AddRow("Log");
        _logPath.style.whiteSpace = WhiteSpace.Normal;
    }

    private void AddDebugTitle()
    {
        _debugTitle = new Label("Debug");
        _debugTitle.style.marginTop = 8;
        _debugTitle.style.flexShrink = 0;
        ApplyLabelStyle(_debugTitle, RowLabelColor, 14, TextAnchor.MiddleLeft, true);
        _panel.Add(_debugTitle);
    }

    private void AddSectionDivider()
    {
        var divider = new VisualElement();
        divider.style.height = 1;
        divider.style.flexShrink = 0;
        divider.style.marginTop = 6;
        divider.style.marginBottom = 6;
        divider.style.backgroundColor = DividerColor;
        _panel.Add(divider);
        _dividers.Add(divider);
    }

    private void ApplyPanelSize()
    {
        if (_panel == null)
            return;

        _panel.style.width = Mathf.Max(MinimumPanelWidth, panelWidth);
        _panel.style.height = Length.Percent(Mathf.Clamp(panelHeightScreenRatio, MinPanelHeightRatio, MaxPanelHeightRatio) * 100f);
    }

    private void ApplyPanelLayout()
    {
        if (_panel == null || _rows.Count == 0)
            return;

        var panelHeight = GetPanelPixelHeight();
        var headerHeight = Mathf.Clamp(panelHeight * HeaderHeightRatio, 30f, 44f);
        var debugHeight = showDebugDetails ? Mathf.Clamp(panelHeight * DebugHeaderHeightRatio, 22f, 34f) : 0f;
        var visibleRows = Mathf.Max(1, CountVisibleRows());
        var visibleDividers = CountVisibleDividers();
        var debugMargin = showDebugDetails ? HeaderMargin : 0f;
        var rowMargins = visibleRows * RowVerticalMargin;
        var dividerHeight = Mathf.Clamp(panelHeight * DividerHeightRatio, 6f, 10f);
        var dividerSpace = visibleDividers * dividerHeight;
        var availableRowHeight = panelHeight - PanelVerticalPadding - headerHeight - HeaderMargin - debugHeight - debugMargin - rowMargins - dividerSpace;
        var rowHeight = Mathf.Clamp(availableRowHeight / visibleRows, 24f, 54f);
        var rowFontSize = Mathf.Clamp(Mathf.RoundToInt(rowHeight * RowFontHeightRatio), 14, 24);

        if (_header != null)
            _header.style.height = headerHeight;
        if (_title != null)
        {
            _title.style.height = headerHeight;
            _title.style.fontSize = Mathf.Clamp(rowFontSize + 4, 18, 28);
        }
        if (_status != null)
        {
            _status.style.height = Mathf.Max(30f, headerHeight - 4f);
            _status.style.fontSize = Mathf.Clamp(rowFontSize, 13, 22);
        }
        if (_debugTitle != null)
        {
            _debugTitle.style.height = debugHeight;
            _debugTitle.style.fontSize = Mathf.Clamp(rowFontSize, 14, 22);
        }

        foreach (var row in _rows)
        {
            row.style.flexBasis = rowHeight;
            row.style.minHeight = rowHeight;
        }

        foreach (var label in _rowLabels)
        {
            label.style.fontSize = rowFontSize;
            label.style.height = Length.Percent(100f);
            label.style.whiteSpace = WhiteSpace.NoWrap;
        }

        foreach (var divider in _dividers)
        {
            divider.style.marginTop = dividerHeight * 0.5f;
            divider.style.marginBottom = dividerHeight * 0.5f;
        }
    }

    private int CountVisibleRows()
    {
        var count = 0;
        foreach (var row in _rows)
        {
            if (row.resolvedStyle.display != DisplayStyle.None)
                count++;
        }
        return count > 0 ? count : _rows.Count;
    }

    private int CountVisibleDividers()
    {
        var count = 0;
        foreach (var divider in _dividers)
        {
            if (divider.resolvedStyle.display != DisplayStyle.None)
                count++;
        }
        return count > 0 ? count : _dividers.Count;
    }

    private float GetPanelPixelHeight()
    {
        var rootHeight = _document != null && _document.rootVisualElement != null
            ? _document.rootVisualElement.resolvedStyle.height
            : 0f;
        if (float.IsNaN(rootHeight) || rootHeight <= 0f)
            rootHeight = Screen.height;

        return rootHeight * Mathf.Clamp(panelHeightScreenRatio, MinPanelHeightRatio, MaxPanelHeightRatio);
    }

    private Label AddRow(string name)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginTop = 2;
        row.style.marginBottom = 2;
        row.style.flexGrow = 1;
        row.style.flexShrink = 1;
        row.style.flexBasis = 0;
        row.style.minHeight = 24;
        _panel.Add(row);
        _rows.Add(row);

        var key = new Label(name);
        ApplyLabelStyle(key, RowLabelColor, 14, TextAnchor.MiddleLeft);
        key.style.width = RowLabelWidth;
        key.style.height = Length.Percent(100f);
        key.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(key);
        _rowLabels.Add(key);

        var value = new Label("-");
        ApplyLabelStyle(value, ValueColor, 14, TextAnchor.MiddleRight);
        value.style.flexGrow = 1;
        value.style.height = Length.Percent(100f);
        value.style.whiteSpace = WhiteSpace.NoWrap;
        row.Add(value);
        _rowLabels.Add(value);
        return value;
    }

    private static void ApplyLabelStyle(Label label, Color color, int fontSize, TextAnchor align, bool bold = false)
    {
        label.style.color = color;
        label.style.fontSize = fontSize;
        label.style.height = 24;
        label.style.unityTextAlign = align;
        label.style.flexShrink = 0;
        label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;

        var font = GetMonitorFont();
        if (font)
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
    }

    private static Font GetMonitorFont()
    {
        if (_monitorFont)
            return _monitorFont;

        var builtinFontNames = new[] { "LegacyRuntime.ttf", "Arial.ttf" };
        foreach (var fontName in builtinFontNames)
        {
            try
            {
                _monitorFont = Resources.GetBuiltinResource<Font>(fontName);
                if (_monitorFont)
                    return _monitorFont;
            }
            catch (ArgumentException)
            {
            }
        }

        return _monitorFont;
    }

    private void Refresh()
    {
        if (!IsPanelReady())
            return;

        if (!receiver)
        {
            SetStatus("No receiver", new Color(0.5f, 0.12f, 0.12f, 1f));
            _port.text = "-";
            _socketFps.text = "-";
            _mainFps.text = "-";
            _mainFps.style.color = Color.white;
            _inputFps.text = "-";
            _appliedFps.text = "-";
            _frame.text = "-";
            _dropped.text = "-";
            _playbackLatency.text = "-";
            _buffer.text = "-";
            _packetAge.text = "-";
            _queue.text = "-";
            _processingErrors.text = "-";
            _validation.text = "-";
            _session.text = "-";
            if (showDebugDetails)
            {
                _packets.text = "-";
                _messages.text = "-";
                _dispatch.text = "-";
                _messageRate.text = "-";
                _lastAddress.text = "-";
                _lastPose.text = "-";
                _background.text = "-";
                _logPath.text = "-";
            }
            return;
        }

        var snapshot = receiver.GetMonitorSnapshot();
        var privateDiagnostics = receiver.GetPrivateDiagnosticsSnapshot();
        var packetAgeSeconds = SecondsSince(snapshot.LastPacketUtcTicks);

        UpdateRates(snapshot);
        UpdateStatus(snapshot, packetAgeSeconds);

        _port.text = string.IsNullOrWhiteSpace(snapshot.BindAddress)
            ? snapshot.ListenPort.ToString()
            : $"{Endpoint(snapshot.BindAddress)}:{snapshot.ListenPort}";
        _socketFps.text = $"{_socketTickRate:0.0}";
        _mainFps.text = $"{_mainThreadFrameRate:0.0}";
        _mainFps.style.color = _hasRateSample ? MainFpsColor(_mainThreadFrameRate) : Color.white;
        _inputFps.text = $"{_inputFrameRate:0.0}";
        _appliedFps.text = $"{_appliedFrameRate:0.0}";
        _frame.text = snapshot.LastFrame == int.MinValue ? "-" : snapshot.LastFrame.ToString();
        _dropped.text = FormatDropped(snapshot);
        _playbackLatency.text = FormatLatency(snapshot.PlaybackLatencyMs);
        _buffer.text = snapshot.BufferedFrameCount == 0 ? "OK" : $"{snapshot.BufferedFrameCount} frames";
        _packetAge.text = FormatAge(packetAgeSeconds);
        _queue.text = snapshot.QueuedMessages == 0 ? "0 pending" : $"{snapshot.QueuedMessages} pending";
        _processingErrors.text = snapshot.ProcessingErrors == 0 ? "0 errors" : $"{snapshot.ProcessingErrors} errors";
        _validation.text = privateDiagnostics.ValidationLogOpen
            ? "logging"
            : (privateDiagnostics.ValidationLoggingEnabled ? "idle" : "off");
        _session.text = string.IsNullOrWhiteSpace(privateDiagnostics.ValidationSessionId) ? "-" : privateDiagnostics.ValidationSessionId;
        if (_session.parent != null)
            _session.parent.style.display = privateDiagnostics.ValidationLogOpen ? DisplayStyle.Flex : DisplayStyle.None;

        if (showDebugDetails)
        {
            _packets.text = snapshot.PacketsReceived.ToString();
            _messages.text = snapshot.MessagesReceived.ToString();
            _dispatch.text = $"{snapshot.MessagesDispatched} / errors {snapshot.ProcessingErrors}";
            _messageRate.text = $"{_messageDispatchRate:0.0} msg/s";
            _lastAddress.text = string.IsNullOrWhiteSpace(snapshot.LastOscAddress) ? "-" : snapshot.LastOscAddress;
            _lastPose.text = string.IsNullOrWhiteSpace(snapshot.LastPoseName) ? "-" : snapshot.LastPoseName;
            _background.text = snapshot.ForceRunInBackground
                ? $"forced {(snapshot.ApplicationRunInBackground ? "on" : "off")}"
                : $"app {(snapshot.ApplicationRunInBackground ? "on" : "off")}";
            _logPath.text = string.IsNullOrWhiteSpace(privateDiagnostics.ValidationLogPath) ? "-" : privateDiagnostics.ValidationLogPath;
        }
    }

    private void UpdateRates(VMCReceiver.MonitorSnapshot snapshot)
    {
        var now = Time.unscaledTime;
        if (_lastRateSampleTime <= 0f)
        {
            _lastRateSampleTime = now;
            _lastSocketTickCount = snapshot.PacketsReceived;
            _lastMainThreadFrameCount = snapshot.MainThreadFrames;
            _lastInputFrameCount = snapshot.InputFramesReceived;
            _lastAppliedFrameCount = snapshot.AppliedFramesReceived;
            _lastDispatchedCount = snapshot.MessagesDispatched;
            return;
        }

        var dt = now - _lastRateSampleTime;
        if (dt <= 0f)
            return;

        _socketTickRate = (snapshot.PacketsReceived - _lastSocketTickCount) / dt;
        _mainThreadFrameRate = (snapshot.MainThreadFrames - _lastMainThreadFrameCount) / dt;
        _inputFrameRate = (snapshot.InputFramesReceived - _lastInputFrameCount) / dt;
        _appliedFrameRate = (snapshot.AppliedFramesReceived - _lastAppliedFrameCount) / dt;
        _messageDispatchRate = (snapshot.MessagesDispatched - _lastDispatchedCount) / dt;
        _hasRateSample = true;
        _lastRateSampleTime = now;
        _lastSocketTickCount = snapshot.PacketsReceived;
        _lastMainThreadFrameCount = snapshot.MainThreadFrames;
        _lastInputFrameCount = snapshot.InputFramesReceived;
        _lastAppliedFrameCount = snapshot.AppliedFramesReceived;
        _lastDispatchedCount = snapshot.MessagesDispatched;
    }

    private void ResetRateSamples()
    {
        _nextRefreshTime = 0f;
        _lastRateSampleTime = 0f;
        _lastSocketTickCount = 0;
        _lastMainThreadFrameCount = 0;
        _lastInputFrameCount = 0;
        _lastAppliedFrameCount = 0;
        _lastDispatchedCount = 0;
        _socketTickRate = 0f;
        _mainThreadFrameRate = 0f;
        _inputFrameRate = 0f;
        _appliedFrameRate = 0f;
        _messageDispatchRate = 0f;
        _hasRateSample = false;
    }

    private void UpdateStatus(VMCReceiver.MonitorSnapshot snapshot, float packetAgeSeconds)
    {
        if (!snapshot.IsRunning)
        {
            SetStatus("Stopped", new Color(0.42f, 0.44f, 0.46f, 1f));
            return;
        }

        if (packetAgeSeconds >= 0f && packetAgeSeconds < 1f)
        {
            SetStatus("Receiving", new Color(0.12f, 0.48f, 0.28f, 1f));
            return;
        }

        SetStatus("Waiting", new Color(0.55f, 0.39f, 0.1f, 1f));
    }

    private void SetStatus(string text, Color color)
    {
        _status.text = text;
        _status.style.backgroundColor = color;
    }

    private static Color MainFpsColor(float fps)
    {
        if (fps < 60f)
            return new Color(1f, 0.24f, 0.22f, 1f);

        if (fps < 90f)
            return new Color(1f, 0.78f, 0.2f, 1f);

        return Color.white;
    }

    private static string Endpoint(string bindAddress)
    {
        return string.IsNullOrWhiteSpace(bindAddress) ? "0.0.0.0" : bindAddress;
    }

    private static float SecondsSince(long utcTicks)
    {
        if (utcTicks <= 0)
            return -1f;

        return Mathf.Max(0f, (float)(DateTime.UtcNow - new DateTime(utcTicks, DateTimeKind.Utc)).TotalSeconds);
    }

    private static string FormatAge(float seconds)
    {
        if (seconds < 0f)
            return "-";

        if (seconds < 1f)
            return $"{seconds * 1000f:0} ms ago";

        return $"{seconds:0.0}s ago";
    }

    private static string FormatLatency(double milliseconds)
    {
        if (milliseconds < 0.0)
            return "-";

        if (milliseconds < 1000.0)
            return $"{milliseconds:0} ms";

        return $"{milliseconds / 1000.0:0.00}s";
    }

    private static string FormatDropped(VMCReceiver.MonitorSnapshot snapshot)
    {
        if (snapshot.DroppedFrameCount == 0)
            return "0";

        if (snapshot.LastDroppedFrameStart == int.MinValue)
            return snapshot.DroppedFrameCount.ToString();

        return $"{snapshot.DroppedFrameCount} (last {snapshot.LastDroppedFrameStart}-{snapshot.LastDroppedFrameEnd})";
    }

    private bool IsPanelReady()
    {
        return _panel != null
            && _status != null
            && _port != null
            && _socketFps != null
            && _mainFps != null
            && _inputFps != null
            && _appliedFps != null
            && _frame != null
            && _dropped != null
            && _playbackLatency != null
            && _buffer != null
            && _packetAge != null
            && _queue != null
            && _processingErrors != null
            && _validation != null
            && _session != null
            && (!showDebugDetails
                || (_packets != null
                    && _messages != null
                    && _dispatch != null
                    && _messageRate != null
                    && _lastAddress != null
                    && _lastPose != null
                    && _background != null
                    && _logPath != null));
    }
}

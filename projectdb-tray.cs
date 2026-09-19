using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml;

namespace ProjectDBTray
{
	internal sealed class ServiceInfo
	{
		public string AppName;
		public string ServiceId;
		public string DisplayName;
		public string ServiceDirectory;
		public string LogDirectory;
		public ServiceControllerStatus? Status;
		public bool Installed;
		public int ProcessId;
		public string LogTimestamp;
		public string LogPid;
		public string LogSource;
		public string LogMessage;
		public string LogFile;
	}

	internal sealed class LibraryInfo
	{
		public bool Installed;
		public int ProcessId;
		public string Path;
		public string Version;
		public string Sha256;
		public string SourceFile;
		public string SourceUrl;
		public string SourceType;
		public DateTime? InstalledAtUtc;
		public long Size;
	}

	internal enum ModernButtonKind
	{
		Primary,
		Secondary,
		Info,
		Danger
	}

	internal static class Ui
	{
		public static readonly Color Background = Color.FromArgb(244, 248, 249);
		public static readonly Color Surface = Color.White;
		public static readonly Color Border = Color.FromArgb(214, 225, 228);
		public static readonly Color Text = Color.FromArgb(24, 41, 48);
		public static readonly Color Muted = Color.FromArgb(101, 119, 126);
		public static readonly Color Accent = Color.FromArgb(34, 160, 171);
		public static readonly Color AccentHover = Color.FromArgb(26, 142, 153);
		public static readonly Color AccentSoft = Color.FromArgb(233, 247, 248);
		public static readonly Color Running = Color.FromArgb(33, 166, 92);
		public static readonly Color Stopped = Color.FromArgb(224, 63, 72);
		public static readonly Color Pending = Color.FromArgb(235, 157, 44);
		public static readonly Color Unknown = Color.FromArgb(117, 132, 139);
		public static readonly Color Footer = Color.FromArgb(20, 38, 48);
		public static readonly Color FooterText = Color.FromArgb(241, 247, 248);
		public static readonly Color FooterMuted = Color.FromArgb(171, 194, 200);

		public static Color EffectiveParentBackColor(Control control, Color fallback)
		{
			Control current = control == null ? null : control.Parent;
			while (current != null)
			{
				Color color = current.BackColor;
				if (color.A == 255) return color;
				current = current.Parent;
			}
			return fallback;
		}

		public static Button Button(string text, bool primary)
		{
			return new ModernButton(text, primary ? ModernButtonKind.Primary : ModernButtonKind.Secondary);
		}

		public static Button InfoButton(string text)
		{
			return new ModernButton(text, ModernButtonKind.Info);
		}

		public static Button DangerButton(string text)
		{
			return new ModernButton(text, ModernButtonKind.Danger);
		}
	}

	internal sealed class ModernButton : Button
	{
		private readonly ModernButtonKind _kind;
		private bool _hover;

		public ModernButton(string text, ModernButtonKind kind)
		{
			_kind = kind;
			Text = text;
			AutoSize = false;
			Height = 36;
			FlatStyle = FlatStyle.Flat;
			FlatAppearance.BorderSize = 0;
			UseVisualStyleBackColor = false;
			Cursor = Cursors.Hand;
			Font = new Font("Segoe UI", 9F, FontStyle.Regular);
			TextAlign = ContentAlignment.MiddleCenter;
			UseCompatibleTextRendering = false;
			Padding = new Padding(0);
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnMouseEnter(EventArgs e)
		{
			_hover = true;
			Invalidate();
			base.OnMouseEnter(e);
		}

		protected override void OnMouseLeave(EventArgs e)
		{
			_hover = false;
			Invalidate();
			base.OnMouseLeave(e);
		}

		protected override void OnEnabledChanged(EventArgs e)
		{
			Invalidate();
			base.OnEnabledChanged(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Color parentColor = Ui.EffectiveParentBackColor(this, Ui.Surface);
			e.Graphics.Clear(parentColor);
			Rectangle rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
			Color back;
			Color border;
			Color text;
			if (!Enabled)
			{
				back = Color.FromArgb(240, 244, 245);
				border = Color.FromArgb(224, 231, 233);
				text = Color.FromArgb(155, 167, 171);
			}
			else if (_kind == ModernButtonKind.Primary)
			{
				back = _hover ? Ui.AccentHover : Ui.Accent;
				border = back;
				text = Color.White;
			}
			else if (_kind == ModernButtonKind.Info)
			{
				back = _hover ? Color.FromArgb(221, 243, 245) : Ui.AccentSoft;
				border = Color.FromArgb(166, 216, 221);
				text = Ui.AccentHover;
			}
			else if (_kind == ModernButtonKind.Danger)
			{
				back = _hover ? Color.FromArgb(254, 241, 242) : Ui.Surface;
				border = Color.FromArgb(236, 187, 191);
				text = Ui.Stopped;
			}
			else
			{
				back = _hover ? Color.FromArgb(247, 251, 252) : Ui.Surface;
				border = Ui.Border;
				text = Ui.Text;
			}
			using (GraphicsPath path = RoundedRectangle(rect, 6))
			using (Brush brush = new SolidBrush(back))
			using (Pen pen = new Pen(border))
			{
				e.Graphics.FillPath(brush, path);
				e.Graphics.DrawPath(pen, path);
			}
			TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
		}

		private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
			Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
			path.AddArc(arc, 180, 90);
			arc.X = rect.Right - diameter;
			path.AddArc(arc, 270, 90);
			arc.Y = rect.Bottom - diameter;
			path.AddArc(arc, 0, 90);
			arc.X = rect.X;
			path.AddArc(arc, 90, 90);
			path.CloseFigure();
			return path;
		}
	}

	internal sealed class StatusDot : Control
	{
		private Color _color = Ui.Unknown;
		public Color DotColor
		{
			get { return _color; }
			set { _color = value; Invalidate(); }
		}

		public StatusDot()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
			Size = new Size(14, 14);
			BackColor = Ui.Surface;
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.Clear(Ui.Surface);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (Brush brush = new SolidBrush(_color))
				e.Graphics.FillEllipse(brush, 1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
		}
	}

	internal class CardPanel : Panel
	{
		private const int Radius = 8;

		public CardPanel()
		{
			BackColor = Ui.Surface;
			Padding = new Padding(1);
			BorderStyle = BorderStyle.None;
			DoubleBuffered = true;
			ResizeRedraw = true;
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}

		protected override void OnResize(EventArgs eventargs)
		{
			base.OnResize(eventargs);
			// Do not assign Region here: rounded Regions cause clipping artefacts while resizing.
			Invalidate();
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			Color parentColor = Ui.EffectiveParentBackColor(this, Ui.Background);
			e.Graphics.Clear(parentColor);
			using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Radius))
			using (Brush brush = new SolidBrush(Ui.Surface))
				e.Graphics.FillPath(brush, path);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1)), Radius))
			using (Pen pen = new Pen(Ui.Border))
				e.Graphics.DrawPath(pen, path);
		}

		private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
		{
			GraphicsPath path = new GraphicsPath();
			int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
			Rectangle arc = new Rectangle(rect.X, rect.Y, diameter, diameter);
			path.AddArc(arc, 180, 90);
			arc.X = rect.Right - diameter;
			path.AddArc(arc, 270, 90);
			arc.Y = rect.Bottom - diameter;
			path.AddArc(arc, 0, 90);
			arc.X = rect.X;
			path.AddArc(arc, 90, 90);
			path.CloseFigure();
			return path;
		}
	}

	internal class BufferedPanel : Panel
	{
		public BufferedPanel()
		{
			DoubleBuffered = true;
			ResizeRedraw = true;
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}
	}

	internal sealed class BufferedLabel : Label
	{
		public BufferedLabel()
		{
			DoubleBuffered = true;
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
		}
	}

	internal sealed class OverlayScrollPanel : BufferedPanel
	{
		private readonly VScrollBar _scrollBar;
		private Control _content;
		private bool _layoutInProgress;

		public OverlayScrollPanel()
		{
			AutoScroll = false;
			TabStop = true;
			SetStyle(ControlStyles.Selectable, true);

			_scrollBar = new VScrollBar();
			_scrollBar.Visible = false;
			_scrollBar.TabStop = false;
			_scrollBar.SmallChange = 32;
			_scrollBar.Scroll += delegate { UpdateContentOffset(); };
			Controls.Add(_scrollBar);

			MouseEnter += delegate { Focus(); };
			MouseWheel += HandleMouseWheel;
		}

		public void SetContent(Control content)
		{
			if (_content != null)
			{
				_content.SizeChanged -= ContentSizeChanged;
				Controls.Remove(_content);
			}
			_content = content;
			if (_content == null) return;

			_content.Dock = DockStyle.None;
			_content.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
			_content.Margin = new Padding(0);
			_content.SizeChanged += ContentSizeChanged;
			Controls.Add(_content);
			_scrollBar.BringToFront();
			RefreshOverlay();
		}

		public void RefreshOverlay()
		{
			UpdateOverlayLayout();
		}

		protected override void OnResize(EventArgs eventargs)
		{
			base.OnResize(eventargs);
			UpdateOverlayLayout();
		}

		protected override void OnLayout(LayoutEventArgs levent)
		{
			base.OnLayout(levent);
			UpdateOverlayLayout();
		}

		private void ContentSizeChanged(object sender, EventArgs e)
		{
			UpdateOverlayLayout();
		}

		private void HandleMouseWheel(object sender, MouseEventArgs e)
		{
			if (!_scrollBar.Visible || e.Delta == 0) return;
			int lines = SystemInformation.MouseWheelScrollLines;
			int step = lines < 0 ? Math.Max(48, ClientSize.Height / 2) : Math.Max(24, lines * 24);
			SetScrollValue(_scrollBar.Value + (e.Delta > 0 ? -step : step));
		}

		private void UpdateOverlayLayout()
		{
			if (_layoutInProgress || _content == null || IsDisposed) return;
			_layoutInProgress = true;
			try
			{
				int width = Math.Max(0, ClientSize.Width);
				if (_content.Width != width) _content.Width = width;

				int maxScroll = Math.Max(0, _content.Height - ClientSize.Height);
				bool visible = maxScroll > 0;
				_scrollBar.Visible = visible;
				if (!visible)
				{
					_scrollBar.Value = 0;
					_content.Location = Point.Empty;
					return;
				}

				int scrollWidth = Math.Max(12, SystemInformation.VerticalScrollBarWidth);
				_scrollBar.SetBounds(Math.Max(0, ClientSize.Width - scrollWidth), 0, scrollWidth, Math.Max(0, ClientSize.Height));
				_scrollBar.LargeChange = Math.Max(1, ClientSize.Height);
				_scrollBar.SmallChange = Math.Max(24, ClientSize.Height / 8);
				_scrollBar.Maximum = maxScroll + _scrollBar.LargeChange - 1;
				if (_scrollBar.Value > maxScroll) _scrollBar.Value = maxScroll;
				UpdateContentOffset();
				_scrollBar.BringToFront();
			}
			finally
			{
				_layoutInProgress = false;
			}
		}

		private void SetScrollValue(int value)
		{
			if (!_scrollBar.Visible || _content == null) return;
			int maxScroll = Math.Max(0, _content.Height - ClientSize.Height);
			value = Math.Max(0, Math.Min(value, maxScroll));
			if (_scrollBar.Value != value) _scrollBar.Value = value;
			UpdateContentOffset();
		}

		private void UpdateContentOffset()
		{
			if (_content == null) return;
			int y = _scrollBar.Visible ? -_scrollBar.Value : 0;
			if (_content.Location.X != 0 || _content.Location.Y != y) _content.Location = new Point(0, y);
		}
	}

	internal sealed class ModernTextBox : UserControl
	{
		private readonly Panel _inner;
		private readonly TextBox _textBox;
		private bool _focused;

		public ModernTextBox()
		{
			Height = 40;
			MinimumSize = new Size(100, 40);
			BackColor = Ui.Border;
			DoubleBuffered = true;
			TabStop = false;

			_inner = new Panel();
			_inner.BackColor = Ui.Surface;
			_inner.TabStop = false;
			Controls.Add(_inner);

			_textBox = new TextBox();
			_textBox.BorderStyle = BorderStyle.None;
			_textBox.Font = new Font("Segoe UI", 9.5F);
			_textBox.BackColor = Ui.Surface;
			_textBox.ForeColor = Ui.Text;
			_textBox.TabStop = true;
			_textBox.GotFocus += delegate { _focused = true; UpdateBorder(); };
			_textBox.LostFocus += delegate { _focused = false; UpdateBorder(); };
			_textBox.TextChanged += delegate { OnTextChanged(EventArgs.Empty); };
			_inner.Controls.Add(_textBox);

			Cursor = Cursors.IBeam;
			Click += delegate { _textBox.Focus(); };
			_inner.Click += delegate { _textBox.Focus(); };
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
			UpdateBorder();
		}

		public override string Text
		{
			get { return _textBox == null ? String.Empty : _textBox.Text; }
			set { if (_textBox != null) _textBox.Text = value ?? String.Empty; }
		}

		public bool UseSystemPasswordChar
		{
			get { return _textBox.UseSystemPasswordChar; }
			set { _textBox.UseSystemPasswordChar = value; }
		}

		private void UpdateBorder()
		{
			BackColor = _focused ? Ui.Accent : Ui.Border;
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			if (_inner == null || _textBox == null) return;
			_inner.SetBounds(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
			int preferred = _textBox.PreferredHeight;
			_textBox.SetBounds(10, Math.Max(2, (_inner.Height - preferred) / 2), Math.Max(20, _inner.Width - 20), preferred);
		}
	}

	internal sealed class ServiceCard : CardPanel
	{
		private readonly StatusDot _dot;
		private readonly Label _name;
		private readonly Label _status;
		private readonly Label _serviceId;
		private readonly Label _activityMeta;
		private readonly Label _logMessage;
		private readonly Button _start;
		private readonly Button _restart;
		private readonly Button _stop;
		private readonly Button _logs;
		private readonly Button _remove;
		private ServiceInfo _service;

		public ServiceCard(Action<ServiceInfo, string> actionHandler, Action<ServiceInfo> logsHandler, Action<ServiceInfo> removeHandler)
		{
			Height = 170;
			MinimumSize = new Size(760, 170);
			Margin = new Padding(0, 0, 0, 14);

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Margin = new Padding(0);
			root.Padding = new Padding(16);
			root.ColumnCount = 2;
			root.RowCount = 2;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.BackColor = Color.Transparent;

			Panel info = new Panel();
			info.Dock = DockStyle.Fill;
			info.Margin = new Padding(0);
			info.BackColor = Ui.Surface;

			_dot = new StatusDot();
			_dot.Location = new Point(3, 5);
			info.Controls.Add(_dot);

			_name = new Label();
			_name.AutoSize = true;
			_name.Font = new Font("Segoe UI Semibold", 11.5F);
			_name.ForeColor = Ui.Text;
			_name.Location = new Point(30, 0);
			info.Controls.Add(_name);

			_status = new Label();
			_status.AutoSize = true;
			_status.Font = new Font("Segoe UI", 9F);
			_status.ForeColor = Ui.Muted;
			_status.Location = new Point(31, 27);
			info.Controls.Add(_status);

			_serviceId = new Label();
			_serviceId.AutoSize = false;
			_serviceId.AutoEllipsis = true;
			_serviceId.Font = new Font("Segoe UI", 8.2F);
			_serviceId.ForeColor = Color.FromArgb(135, 149, 154);
			_serviceId.Location = new Point(31, 47);
			_serviceId.Size = new Size(520, 18);
			info.Controls.Add(_serviceId);
			root.Controls.Add(info, 0, 0);

			TableLayoutPanel actions = new TableLayoutPanel();
			actions.Dock = DockStyle.Top;
			actions.Height = 36;
			actions.Margin = new Padding(0, 4, 5, 0);
			actions.Padding = new Padding(0);
			actions.ColumnCount = 5;
			actions.RowCount = 1;
			actions.BackColor = Ui.Surface;
			for (int i = 0; i < 5; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

			_start = Ui.Button("Start", false);
			_restart = Ui.Button("Restart", false);
			_stop = Ui.Button("Stop", false);
			_logs = Ui.InfoButton("Logs");
			_remove = Ui.DangerButton("Remove");
			Button[] buttons = new Button[] { _start, _restart, _stop, _logs, _remove };
			for (int i = 0; i < buttons.Length; i++)
			{
				buttons[i].Dock = DockStyle.Fill;
				buttons[i].Margin = new Padding(i == 0 ? 0 : 4, 0, i == buttons.Length - 1 ? 0 : 4, 0);
				actions.Controls.Add(buttons[i], i, 0);
			}
			root.Controls.Add(actions, 1, 0);

			TableLayoutPanel logPreview = new TableLayoutPanel();
			logPreview.Dock = DockStyle.Fill;
			logPreview.Margin = new Padding(32, 10, 5, 5);
			logPreview.Padding = new Padding(10);
			logPreview.ColumnCount = 1;
			logPreview.RowCount = 2;
			logPreview.RowStyles.Add(new RowStyle(SizeType.Absolute, 17F));
			logPreview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			logPreview.BackColor = Color.FromArgb(248, 251, 252);

			_activityMeta = new Label();
			_activityMeta.Dock = DockStyle.Fill;
			_activityMeta.Font = new Font("Segoe UI", 8F);
			_activityMeta.ForeColor = Ui.Muted;
			_activityMeta.TextAlign = ContentAlignment.MiddleLeft;
			_activityMeta.AutoEllipsis = true;

			_logMessage = new Label();
			_logMessage.Dock = DockStyle.Fill;
			_logMessage.Font = new Font("Segoe UI", 9F);
			_logMessage.ForeColor = Ui.Text;
			_logMessage.TextAlign = ContentAlignment.MiddleLeft;
			_logMessage.AutoEllipsis = true;

			logPreview.Controls.Add(_activityMeta, 0, 0);
			logPreview.Controls.Add(_logMessage, 0, 1);
			root.Controls.Add(logPreview, 0, 1);
			root.SetColumnSpan(logPreview, 2);

			Controls.Add(root);

			_start.Click += delegate { if (_service != null) actionHandler(_service, "start"); };
			_restart.Click += delegate { if (_service != null) actionHandler(_service, "restart"); };
			_stop.Click += delegate { if (_service != null) actionHandler(_service, "stop"); };
			_logs.Click += delegate { if (_service != null) logsHandler(_service); };
			_remove.Click += delegate { if (_service != null) removeHandler(_service); };
		}

		public void UpdateService(ServiceInfo service, bool busy)
		{
			_service = service;
			_name.Text = service.AppName;
			_status.Text = StatusText(service);
			_dot.DotColor = StatusColor(service);

			List<string> details = new List<string>();
			details.Add(service.ServiceId);
			if (service.Status == ServiceControllerStatus.Running)
			{
				int pid = 0;
				if (!String.IsNullOrWhiteSpace(service.LogPid))
				{
					int parsedPid;
					if (Int32.TryParse(service.LogPid, out parsedPid)) pid = parsedPid;
				}
				if (pid <= 0) pid = service.ProcessId;
				if (pid > 0) details.Add(pid.ToString(CultureInfo.InvariantCulture));
				if (!String.IsNullOrWhiteSpace(service.LogSource)) details.Add(service.LogSource);
			}
			_serviceId.Text = String.Join(" \u00B7 ", details.ToArray());

			if (String.IsNullOrWhiteSpace(service.LogMessage))
			{
				_activityMeta.Text = "Last activity \u00B7 no structured log entry available";
				_logMessage.Text = String.Empty;
				_logMessage.ForeColor = Ui.Muted;
			}
			else
			{
				_activityMeta.Text = String.IsNullOrWhiteSpace(service.LogTimestamp) ?
					"Last activity" : "Last activity \u00B7 " + service.LogTimestamp;
				_logMessage.Text = service.LogMessage;
				string lower = service.LogMessage.ToLowerInvariant();
				_logMessage.ForeColor = lower.Contains("error") || lower.Contains("failed") || lower.Contains("fatal") || lower.Contains("exception") ? Ui.Stopped :
					(lower.Contains("warn") ? Ui.Pending : Ui.Text);
			}

			bool pending = IsPending(service.Status);
			_start.Enabled = !busy && service.Installed && service.Status != ServiceControllerStatus.Running && !pending;
			_restart.Enabled = !busy && service.Installed && service.Status == ServiceControllerStatus.Running;
			_stop.Enabled = !busy && service.Installed && (service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.Paused);
			_logs.Enabled = true;
			_remove.Enabled = true;
		}

		private static bool IsPending(ServiceControllerStatus? status)
		{
			return status == ServiceControllerStatus.StartPending || status == ServiceControllerStatus.StopPending ||
				status == ServiceControllerStatus.PausePending || status == ServiceControllerStatus.ContinuePending;
		}

		private static Color StatusColor(ServiceInfo service)
		{
			if (!service.Installed || !service.Status.HasValue) return Ui.Unknown;
			if (service.Status == ServiceControllerStatus.Running)
				return IsInitializedSource(service.LogSource) ? Ui.Running : Ui.Pending;
			if (IsPending(service.Status)) return Ui.Pending;
			return Ui.Stopped;
		}

		private static bool IsInitializedSource(string source)
		{
			if (String.IsNullOrWhiteSpace(source)) return false;
			string value = source.Trim();
			return !Regex.IsMatch(value, @"(?:^|\.)0$");
		}

		private static string StatusText(ServiceInfo service)
		{
			if (!service.Installed || !service.Status.HasValue) return "Service is not installed";
			switch (service.Status.Value)
			{
				case ServiceControllerStatus.Running: return "Running";
				case ServiceControllerStatus.Stopped: return "Stopped";
				case ServiceControllerStatus.StartPending: return "Starting...";
				case ServiceControllerStatus.StopPending: return "Stopping...";
				case ServiceControllerStatus.Paused: return "Paused";
				case ServiceControllerStatus.PausePending: return "Pausing...";
				case ServiceControllerStatus.ContinuePending: return "Continuing...";
				default: return service.Status.Value.ToString();
			}
		}
	}

	internal sealed class ManagerForm : Form
	{
		public static readonly int ExternalShowMessage = RegisterWindowMessage("ProjectDB.ServiceManager.Show.v1");
		private readonly OverlayScrollPanel _servicesPanel;
		private readonly TableLayoutPanel _servicesTable;
		private readonly Label _summary;
		private readonly Label _busyLabel;
		private readonly Label _footerInfo;
		private readonly ToolTip _libraryToolTip = new ToolTip();
		private readonly Button _addButton;
		private readonly Button _restartAllButton;
		private readonly Button _removeAllButton;
		private readonly Label _libraryStatus;
		private readonly Button _libraryInstallButton;
		private readonly Button _libraryRemoveButton;
		private readonly Dictionary<string, ServiceCard> _cards = new Dictionary<string, ServiceCard>(StringComparer.OrdinalIgnoreCase);
		private readonly Action _addHandler;
		private readonly Action _restartAllHandler;
		private readonly Action _removeAllHandler;
		private readonly Action<ServiceInfo, string> _serviceAction;
		private readonly Action<ServiceInfo> _logsHandler;
		private readonly Action<ServiceInfo> _removeHandler;
		private readonly Action _libraryInstallHandler;
		private readonly Action _libraryRemoveHandler;
		private bool _allowClose;
		private bool _busy;
		private bool _libraryBusy;
		private bool _libraryControlsAvailableWhileBusy;

		public ManagerForm(Icon icon, Image headerImage, Action addHandler, Action restartAllHandler, Action removeAllHandler,
			Action<ServiceInfo, string> serviceAction, Action<ServiceInfo> logsHandler, Action<ServiceInfo> removeHandler,
			Action libraryInstallHandler, Action libraryRemoveHandler)
		{
			_addHandler = addHandler;
			_restartAllHandler = restartAllHandler;
			_removeAllHandler = removeAllHandler;
			_serviceAction = serviceAction;
			_logsHandler = logsHandler;
			_removeHandler = removeHandler;
			_libraryInstallHandler = libraryInstallHandler;
			_libraryRemoveHandler = libraryRemoveHandler;

			Text = "ProjectDB Service Manager";
			Icon = icon;
			StartPosition = FormStartPosition.CenterScreen;
			MinimumSize = new Size(900, 650);
			Size = new Size(1040, 720);
			BackColor = Ui.Background;
			Font = new Font("Segoe UI", 9F);
			ShowInTaskbar = true;
			AutoScaleMode = AutoScaleMode.Dpi;
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Margin = new Padding(0);
			root.Padding = new Padding(0);
			root.ColumnCount = 1;
			root.RowCount = 3;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
			root.BackColor = Ui.Background;

			TableLayoutPanel header = new TableLayoutPanel();
			header.Dock = DockStyle.Fill;
			header.Margin = new Padding(0);
			header.Padding = new Padding(0);
			header.ColumnCount = 1;
			header.RowCount = 3;
			header.RowStyles.Add(new RowStyle(SizeType.Absolute, 98F));
			header.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
			header.RowStyles.Add(new RowStyle(SizeType.Absolute, 81F));
			header.BackColor = Ui.Surface;

			TableLayoutPanel headerMain = new TableLayoutPanel();
			headerMain.Dock = DockStyle.Fill;
			headerMain.Margin = new Padding(0);
			headerMain.Padding = new Padding(28, 10, 30, 10);
			headerMain.ColumnCount = 3;
			headerMain.RowCount = 1;
			headerMain.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58F));
			headerMain.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			headerMain.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390F));
			headerMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			PictureBox picture = new PictureBox();
			picture.Image = headerImage;
			picture.SizeMode = PictureBoxSizeMode.CenterImage;
			picture.Dock = DockStyle.Fill;
			picture.Margin = new Padding(0, 0, 10, 0);
			headerMain.Controls.Add(picture, 0, 0);

			TableLayoutPanel titleLayout = new TableLayoutPanel();
			titleLayout.Dock = DockStyle.Fill;
			titleLayout.Margin = new Padding(0);
			titleLayout.Padding = new Padding(0, 12, 0, 10);
			titleLayout.ColumnCount = 1;
			titleLayout.RowCount = 2;
			titleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			titleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
			titleLayout.BackColor = Ui.Surface;
			Label title = new Label();
			title.Text = "ProjectDB Service Manager";
			title.Font = new Font("Segoe UI Semibold", 16.5F);
			title.ForeColor = Ui.Text;
			title.Dock = DockStyle.Fill;
			title.Margin = new Padding(7, 0, 0, 0);
			title.TextAlign = ContentAlignment.MiddleLeft;
			title.AutoEllipsis = true;
			titleLayout.Controls.Add(title, 0, 0);
			_summary = new Label();
			_summary.Text = "Loading services...";
			_summary.Font = new Font("Segoe UI", 9F);
			_summary.ForeColor = Ui.Muted;
			_summary.Dock = DockStyle.Fill;
			_summary.Margin = new Padding(10, 3, 0, 0);
			_summary.TextAlign = ContentAlignment.TopLeft;
			_summary.AutoEllipsis = true;
			titleLayout.Controls.Add(_summary, 0, 1);
			headerMain.Controls.Add(titleLayout, 1, 0);

			TableLayoutPanel topActions = new TableLayoutPanel();
			topActions.Dock = DockStyle.Fill;
			topActions.Margin = new Padding(0);
			topActions.Padding = new Padding(0, 20, 0, 20);
			topActions.ColumnCount = 3;
			topActions.RowCount = 1;
			for (int i = 0; i < 3; i++) topActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
			topActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			topActions.BackColor = Ui.Surface;
			_addButton = Ui.Button("Add application", true);
			_restartAllButton = Ui.Button("Restart all", false);
			_removeAllButton = Ui.DangerButton("Remove all");
			Button[] topButtons = new Button[] { _addButton, _restartAllButton, _removeAllButton };
			for (int i = 0; i < topButtons.Length; i++)
			{
				topButtons[i].Dock = DockStyle.Fill;
				topButtons[i].Margin = new Padding(i == 0 ? 0 : 5, 0, i == topButtons.Length - 1 ? 0 : 5, 0);
				topActions.Controls.Add(topButtons[i], i, 0);
			}
			_addButton.Click += delegate { _addHandler(); };
			_restartAllButton.Click += delegate { _restartAllHandler(); };
			_removeAllButton.Click += delegate { _removeAllHandler(); };
			headerMain.Controls.Add(topActions, 2, 0);
			header.Controls.Add(headerMain, 0, 0);
			Panel headerDivider = new Panel();
			headerDivider.Dock = DockStyle.Fill;
			headerDivider.Margin = new Padding(0);
			headerDivider.BackColor = Ui.Border;
			header.Controls.Add(headerDivider, 0, 1);

			TableLayoutPanel librarySection = new TableLayoutPanel();
			librarySection.Dock = DockStyle.Fill;
			librarySection.Margin = new Padding(0);
			librarySection.Padding = new Padding(0);
			librarySection.ColumnCount = 1;
			librarySection.RowCount = 2;
			librarySection.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			librarySection.RowStyles.Add(new RowStyle(SizeType.Absolute, 1F));
			librarySection.BackColor = Ui.Surface;

			TableLayoutPanel libraryLayout = new TableLayoutPanel();
			libraryLayout.Dock = DockStyle.Fill;
			libraryLayout.Margin = new Padding(0);
			libraryLayout.Padding = new Padding(28, 9, 30, 9);
			libraryLayout.ColumnCount = 2;
			libraryLayout.RowCount = 1;
			libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			libraryLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238F));
			libraryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			TableLayoutPanel libraryText = new TableLayoutPanel();
			libraryText.Dock = DockStyle.Fill;
			libraryText.Margin = new Padding(0);
			libraryText.Padding = new Padding(0, 4, 0, 2);
			libraryText.ColumnCount = 1;
			libraryText.RowCount = 2;
			libraryText.RowStyles.Add(new RowStyle(SizeType.Absolute, 26F));
			libraryText.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
			libraryText.BackColor = Ui.Surface;
			Label libraryTitle = new Label();
			libraryTitle.Text = "Application library override";
			libraryTitle.Font = new Font("Segoe UI Semibold", 9.5F);
			libraryTitle.ForeColor = Ui.Text;
			libraryTitle.Dock = DockStyle.Fill;
			libraryTitle.Margin = new Padding(0);
			libraryTitle.TextAlign = ContentAlignment.MiddleLeft;
			libraryText.Controls.Add(libraryTitle, 0, 0);
			_libraryStatus = new Label();
			_libraryStatus.Text = "Checking local library...";
			_libraryStatus.Font = new Font("Segoe UI", 8.5F);
			_libraryStatus.ForeColor = Ui.Muted;
			_libraryStatus.Dock = DockStyle.Fill;
			_libraryStatus.Margin = new Padding(0, 4, 0, 0);
			_libraryStatus.TextAlign = ContentAlignment.TopLeft;
			_libraryStatus.AutoEllipsis = true;
			libraryText.Controls.Add(_libraryStatus, 0, 1);
			libraryLayout.Controls.Add(libraryText, 0, 0);

			TableLayoutPanel libraryActions = new TableLayoutPanel();
			libraryActions.Dock = DockStyle.Fill;
			libraryActions.Margin = new Padding(0);
			libraryActions.Padding = new Padding(0, 14, 0, 14);
			libraryActions.ColumnCount = 2;
			libraryActions.RowCount = 1;
			libraryActions.BackColor = Ui.Surface;
			libraryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
			libraryActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
			_libraryInstallButton = Ui.Button("Select file", false);
			_libraryRemoveButton = Ui.DangerButton("Remove");
			_libraryInstallButton.Dock = DockStyle.Fill;
			_libraryRemoveButton.Dock = DockStyle.Fill;
			_libraryInstallButton.Margin = new Padding(0, 0, 5, 0);
			_libraryRemoveButton.Margin = new Padding(5, 0, 0, 0);
			_libraryInstallButton.Click += delegate { _libraryInstallHandler(); };
			_libraryRemoveButton.Click += delegate { _libraryRemoveHandler(); };
			libraryActions.Controls.Add(_libraryInstallButton, 0, 0);
			libraryActions.Controls.Add(_libraryRemoveButton, 1, 0);
			libraryLayout.Controls.Add(libraryActions, 1, 0);
			librarySection.Controls.Add(libraryLayout, 0, 0);
			Panel libraryBottomLine = new Panel();
			libraryBottomLine.Dock = DockStyle.Fill;
			libraryBottomLine.Margin = new Padding(0);
			libraryBottomLine.BackColor = Ui.Border;
			librarySection.Controls.Add(libraryBottomLine, 0, 1);
			header.Controls.Add(librarySection, 0, 2);
			root.Controls.Add(header, 0, 0);

			_servicesPanel = new OverlayScrollPanel();
			_servicesPanel.Dock = DockStyle.Fill;
			_servicesPanel.Margin = new Padding(0);
			_servicesPanel.BackColor = Ui.Background;
			_servicesTable = new TableLayoutPanel();
			_servicesTable.Dock = DockStyle.Top;
			_servicesTable.AutoSize = true;
			_servicesTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			_servicesTable.ColumnCount = 1;
			_servicesTable.RowCount = 0;
			_servicesTable.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
			_servicesTable.Padding = new Padding(28, 28, 28, 28);
			_servicesTable.BackColor = Ui.Background;
			_servicesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			_servicesPanel.SetContent(_servicesTable);
			root.Controls.Add(_servicesPanel, 0, 1);

			TableLayoutPanel footer = new TableLayoutPanel();
			footer.Dock = DockStyle.Fill;
			footer.Margin = new Padding(0);
			footer.Padding = new Padding(24, 0, 24, 0);
			footer.BackColor = Ui.Footer;
			footer.ColumnCount = 2;
			footer.RowCount = 1;
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
			footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
			_busyLabel = new BufferedLabel();
			_busyLabel.Text = "Ready";
			_busyLabel.Dock = DockStyle.Fill;
			_busyLabel.ForeColor = Ui.FooterText;
			_busyLabel.TextAlign = ContentAlignment.MiddleLeft;
			_busyLabel.AutoEllipsis = true;
			footer.Controls.Add(_busyLabel, 0, 0);
			_footerInfo = new BufferedLabel();
			_footerInfo.Text = "\u00A9 ProjectDB, 2026  \u00B7  Service Manager v0.1.0-dev  \u00B7  Core 3.4.0";
			_footerInfo.Dock = DockStyle.Fill;
			_footerInfo.ForeColor = Ui.FooterMuted;
			_footerInfo.Font = new Font("Segoe UI", 8.5F);
			_footerInfo.TextAlign = ContentAlignment.MiddleRight;
			footer.Controls.Add(_footerInfo, 1, 0);
			root.Controls.Add(footer, 0, 2);

			Controls.Add(root);
			FormClosing += ManagerFormClosing;
		}

		private void ManagerFormClosing(object sender, FormClosingEventArgs e)
		{
			if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
			{
				e.Cancel = true;
				Hide();
			}
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == ExternalShowMessage)
			{
				BeginInvoke((MethodInvoker)delegate { RestoreFromExternalLaunch(); });
			}
			base.WndProc(ref m);
		}

		private void RestoreFromExternalLaunch()
		{
			if (!Visible) Show();
			if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
			ShowInTaskbar = true;
			Activate();
			BringToFront();
		}

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern int RegisterWindowMessage(string message);

		public void AllowClose() { _allowClose = true; }

		public void SetBusy(bool busy, string text)
		{
			_busy = busy;
			SetStatusText(text);
			_addButton.Enabled = !busy;
			_restartAllButton.Enabled = !busy && _restartAllButton.Tag != null;
			_removeAllButton.Enabled = !busy && _removeAllButton.Tag != null;
			bool libraryEnabled = !_libraryBusy || _libraryControlsAvailableWhileBusy;
			_libraryInstallButton.Enabled = !busy && libraryEnabled;
			_libraryRemoveButton.Enabled = !busy && libraryEnabled && _libraryRemoveButton.Tag != null;
		}

		public void SetLibraryBusy(bool busy, string text)
		{
			_libraryBusy = busy;
			if (busy) _libraryControlsAvailableWhileBusy = false;
			SetStatusText(text);
			bool libraryEnabled = !busy || _libraryControlsAvailableWhileBusy;
			_libraryInstallButton.Enabled = !_busy && libraryEnabled;
			_libraryRemoveButton.Enabled = !_busy && libraryEnabled && _libraryRemoveButton.Tag != null;
		}

		public void SetLibraryControlsAvailableWhileBusy(bool available)
		{
			_libraryControlsAvailableWhileBusy = available;
			bool libraryEnabled = !_libraryBusy || available;
			_libraryInstallButton.Enabled = !_busy && libraryEnabled;
			_libraryRemoveButton.Enabled = !_busy && libraryEnabled && _libraryRemoveButton.Tag != null;
		}

		private void SetStatusText(string text)
		{
			if (text != null)
			{
				string next = String.IsNullOrWhiteSpace(text) ? "Ready" : text;
				if (!String.Equals(_busyLabel.Text, next, StringComparison.Ordinal))
				{
					_busyLabel.Text = next;
					_busyLabel.Refresh();
				}
			}
			UpdateOperationCursor();
		}

		private void UpdateOperationCursor()
		{
			string status = _busyLabel == null || _busyLabel.Text == null ? String.Empty : _busyLabel.Text.TrimEnd();
			bool waiting = status.EndsWith("...", StringComparison.Ordinal);
			UseWaitCursor = false;
			Cursor next = waiting ? Cursors.AppStarting : Cursors.Default;
			if (Cursor != next) Cursor = next;
			if (!waiting) Cursor.Current = Cursors.Default;
		}

		public void UpdateServices(List<ServiceInfo> services)
		{
			HashSet<string> present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int running = 0;
			bool structureChanged = false;
			foreach (ServiceInfo service in services)
			{
				present.Add(service.ServiceId);
				if (service.Installed && service.Status == ServiceControllerStatus.Running) running++;
				ServiceCard card;
				if (!_cards.TryGetValue(service.ServiceId, out card))
				{
					card = new ServiceCard(_serviceAction, _logsHandler, _removeHandler);
					_cards[service.ServiceId] = card;
					structureChanged = true;
				}
				card.UpdateService(service, false);
			}
			List<string> remove = new List<string>();
			foreach (string id in _cards.Keys) if (!present.Contains(id)) remove.Add(id);
			foreach (string id in remove)
			{
				ServiceCard card = _cards[id];
				_cards.Remove(id);
				card.Dispose();
				structureChanged = true;
			}
			if (structureChanged) RebuildCardLayout(services);
			_summary.Text = services.Count == 0 ? "No ProjectDB applications registered" :
				services.Count + " application" + (services.Count == 1 ? String.Empty : "s") + " \u00B7 " + running + " running";
			_restartAllButton.Tag = running > 0 ? (object)true : null;
			_removeAllButton.Tag = services.Count > 0 ? (object)true : null;
			_restartAllButton.Enabled = !_busy && running > 0;
			_removeAllButton.Enabled = !_busy && services.Count > 0;
			UpdateOperationCursor();
		}

		private void RebuildCardLayout(List<ServiceInfo> services)
		{
			_servicesTable.SuspendLayout();
			_servicesTable.Controls.Clear();
			_servicesTable.RowStyles.Clear();
			_servicesTable.RowCount = 0;
			for (int index = 0; index < services.Count; index++)
			{
				ServiceInfo service = services[index];
				ServiceCard card;
				if (!_cards.TryGetValue(service.ServiceId, out card)) continue;
				bool last = index == services.Count - 1;
				int row = _servicesTable.RowCount++;
				_servicesTable.RowStyles.Add(new RowStyle(SizeType.Absolute, last ? 170F : 184F));
				card.Dock = DockStyle.Fill;
				card.Margin = new Padding(0, 0, 0, last ? 0 : 14);
				_servicesTable.Controls.Add(card, 0, row);
			}
			_servicesTable.ResumeLayout(true);
			_servicesPanel.RefreshOverlay();
			_servicesPanel.Invalidate(true);
		}

		public void UpdateLibrary(LibraryInfo library, bool controlsAvailableWhileBusy)
		{
			_libraryControlsAvailableWhileBusy = controlsAvailableWhileBusy;
			if (library != null && library.Installed)
			{
				string version = String.IsNullOrWhiteSpace(library.Version) ? String.Empty : library.Version;
				List<string> pieces = new List<string>();
				pieces.Add(String.IsNullOrWhiteSpace(version) ? "Local library" : "Local library " + version);
				pieces.Add(FormatSize(library.Size));
				if (!String.IsNullOrWhiteSpace(library.Sha256))
				{
					string shownHash = library.Sha256.Length > 24 ? library.Sha256.Substring(0, 24) + "..." : library.Sha256;
					pieces.Add("SHA-256 " + shownHash);
				}
				_libraryStatus.Text = String.Join(" \u00B7 ", pieces.ToArray());
				_libraryToolTip.SetToolTip(_libraryStatus, String.IsNullOrWhiteSpace(library.Sha256) ? _libraryStatus.Text : "SHA-256 " + library.Sha256);
				_libraryInstallButton.Text = "Replace file";
				_libraryRemoveButton.Tag = library;
				bool libraryEnabled = !_libraryBusy || _libraryControlsAvailableWhileBusy;
				_libraryInstallButton.Enabled = !_busy && libraryEnabled;
				_libraryRemoveButton.Enabled = !_busy && libraryEnabled;
			}
			else
			{
				_libraryStatus.Text = "Automatic release/cache selection is active";
				_libraryToolTip.SetToolTip(_libraryStatus, String.Empty);
				_libraryInstallButton.Text = "Select file";
				_libraryRemoveButton.Tag = null;
				_libraryInstallButton.Enabled = !_busy && (!_libraryBusy || _libraryControlsAvailableWhileBusy);
				_libraryRemoveButton.Enabled = false;
			}
		}

		private static string FormatSize(long size)
		{
			if (size < 1024) return size + " B";
			if (size < 1024 * 1024) return (size / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
			return (size / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
		}
	}

	internal sealed class AddApplicationForm : Form
	{
		private readonly ModernTextBox _host;
		private readonly ModernTextBox _app;
		private readonly ModernTextBox _password;
		private readonly Button _register;

		public string HostValue { get; private set; }
		public string AppNameValue { get; private set; }
		public string PasswordValue { get; private set; }

		public AddApplicationForm(Icon icon)
		{
			Text = "Add ProjectDB application";
			Icon = CreateInsetWindowIcon(icon);
			StartPosition = FormStartPosition.CenterParent;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ClientSize = new Size(560, 414);
			BackColor = Ui.Surface;
			Font = new Font("Segoe UI", 9F);
			AutoScaleMode = AutoScaleMode.Dpi;

			TableLayoutPanel root = new TableLayoutPanel();
			root.Dock = DockStyle.Fill;
			root.Margin = new Padding(0);
			root.Padding = new Padding(27, 24, 32, 22);
			root.ColumnCount = 1;
			root.RowCount = 13;
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
			root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

			Label title = new Label();
			title.Text = "Register a new application";
			title.Font = new Font("Segoe UI Semibold", 15F);
			title.ForeColor = Ui.Text;
			title.Dock = DockStyle.Fill;
			title.Margin = new Padding(0, 0, 0, 0);
			title.TextAlign = ContentAlignment.MiddleLeft;
			root.Controls.Add(title, 0, 0);

			Label note = new Label();
			note.Text = "ProjectDB binaries are reused. Only a new cli.json and Windows service are created.";
			note.ForeColor = Ui.Muted;
			note.Font = new Font("Segoe UI", 8.8F);
			note.Dock = DockStyle.Fill;
			note.Margin = new Padding(3, 4, 0, 0);
			note.TextAlign = ContentAlignment.TopLeft;
			note.AutoEllipsis = true;
			root.Controls.Add(note, 0, 1);

			Label hostLabel = CreateFieldLabel("ProjectDB server address");
			root.Controls.Add(hostLabel, 0, 3);
			_host = CreateTextField(false);
			_host.Text = "node.projectdb.pro";
			root.Controls.Add(_host, 0, 4);

			Label appLabel = CreateFieldLabel("Application name");
			root.Controls.Add(appLabel, 0, 6);
			_app = CreateTextField(false);
			root.Controls.Add(_app, 0, 7);

			Label passwordLabel = CreateFieldLabel("Access password");
			root.Controls.Add(passwordLabel, 0, 9);
			_password = CreateTextField(true);
			root.Controls.Add(_password, 0, 10);

			TableLayoutPanel buttons = new TableLayoutPanel();
			buttons.Dock = DockStyle.Fill;
			buttons.Margin = new Padding(4, 0, 0, 0);
			buttons.ColumnCount = 3;
			buttons.RowCount = 1;
			buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
			buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112F));
			buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 102F));
			buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			_register = Ui.Button("Register", true);
			_register.Dock = DockStyle.Fill;
			_register.Margin = new Padding(0, 0, 8, 0);
			_register.Click += RegisterClick;
			buttons.Controls.Add(_register, 1, 0);

			Button cancel = Ui.Button("Cancel", false);
			cancel.Dock = DockStyle.Fill;
			cancel.Margin = new Padding(0);
			cancel.Click += delegate { Close(); };
			buttons.Controls.Add(cancel, 2, 0);
			root.Controls.Add(buttons, 0, 12);

			Controls.Add(root);
			CancelButton = cancel;
			AcceptButton = _register;
		}

		private static Label CreateFieldLabel(string text)
		{
			Label label = new Label();
			label.Text = text;
			label.Font = new Font("Segoe UI", 9F);
			label.ForeColor = Ui.Text;
			label.Dock = DockStyle.Fill;
			label.Margin = new Padding(3, 0, 0, 4);
			label.TextAlign = ContentAlignment.MiddleLeft;
			return label;
		}

		private static ModernTextBox CreateTextField(bool password)
		{
			ModernTextBox box = new ModernTextBox();
			box.Dock = DockStyle.Fill;
			box.Margin = new Padding(5, 0, 0, 0);
			box.UseSystemPasswordChar = password;
			return box;
		}

		private static Icon CreateInsetWindowIcon(Icon source)
		{
			if (source == null) return null;
			try { return new Icon(source, new Size(16, 16)); }
			catch { return (Icon)source.Clone(); }
		}

		[DllImport("user32.dll")]
		private static extern bool DestroyIcon(IntPtr handle);

		private void RegisterClick(object sender, EventArgs e)
		{
			string host = _host.Text.Trim();
			string appName = _app.Text.Trim();
			string password = _password.Text;
			if (String.IsNullOrWhiteSpace(host))
			{
				MessageBox.Show(this, "Enter the server address.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			if (!Regex.IsMatch(appName, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$"))
			{
				MessageBox.Show(this, "Application name: up to 100 letters, digits, dots, underscores or hyphens.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			if (String.IsNullOrEmpty(password))
			{
				MessageBox.Show(this, "Password cannot be empty.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			HostValue = host;
			AppNameValue = appName;
			PasswordValue = password;
			_password.Text = String.Empty;
			DialogResult = DialogResult.OK;
			Close();
		}
	}

	internal sealed class TrayApplicationContext : ApplicationContext
	{
		private readonly string _baseDirectory;
		private readonly string _binDirectory;
		private readonly string _serviceRoot;
		private readonly string _controlHelperPath;
		private readonly string _uninstallHelperPath;
		private readonly string _libraryDirectory;
		private readonly string _libraryPath;
		private readonly string _libraryMetadataPath;
		private readonly string _iconPath;
		private readonly NotifyIcon _notifyIcon;
		private readonly ContextMenuStrip _menu;
		private readonly System.Windows.Forms.Timer _timer;
		private readonly EventWaitHandle _showEvent;
		private readonly RegisteredWaitHandle _showWait;
		private readonly Dictionary<string, string> _lastStates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Icon _baseIcon;
		private readonly Icon _windowIcon;
		private readonly Icon _iconRunning;
		private readonly Icon _iconStopped;
		private readonly Icon _iconPending;
		private readonly Icon _iconUnknown;
		private readonly ManagerForm _manager;
		private List<ServiceInfo> _services = new List<ServiceInfo>();
		private LibraryInfo _library = new LibraryInfo();
		private bool _initialized;
		private bool _commandRunning;
		private sealed class PendingLibraryOperation
		{
			public string Command;
			public string Value;
			public string BusyText;
			public string ExpectedSha;
			public bool ExpectRemoved;
			public Action<int> Completed;
		}

		private bool _libraryCommandRunning;
		private string _libraryExpectedSha;
		private bool _libraryExpectRemoved;
		private PendingLibraryOperation _pendingLibraryOperation;
		private bool _exiting;

		public TrayApplicationContext(EventWaitHandle showEvent)
		{
			_showEvent = showEvent;
			string executableDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			_baseDirectory = String.Equals(new DirectoryInfo(executableDirectory).Name, "bin", StringComparison.OrdinalIgnoreCase)
				? Directory.GetParent(executableDirectory).FullName
				: executableDirectory;
			_binDirectory = Path.Combine(_baseDirectory, "bin");
			_serviceRoot = Path.Combine(_baseDirectory, "service");
			_controlHelperPath = Path.Combine(_binDirectory, "projectdb-service-control.exe");
			_uninstallHelperPath = Path.Combine(_binDirectory, "projectdb-uninstall.exe");
			_libraryDirectory = Path.Combine(_baseDirectory, "lib");
			_libraryPath = Path.Combine(_libraryDirectory, "app.so");
			_libraryMetadataPath = Path.Combine(_libraryDirectory, "app.so.meta.json");
			_iconPath = Path.Combine(_baseDirectory, "projectdb.ico");

			_baseIcon = LoadBaseIcon(_baseDirectory, _iconPath);
			_windowIcon = LoadWindowIcon(_iconPath, _baseIcon);
			_iconRunning = CreateStatusIcon(_baseIcon, Ui.Running);
			_iconStopped = CreateStatusIcon(_baseIcon, Ui.Stopped);
			_iconPending = CreateStatusIcon(_baseIcon, Ui.Pending);
			_iconUnknown = CreateStatusIcon(_baseIcon, Ui.Unknown);

			_manager = new ManagerForm(_windowIcon, LoadHeaderImage(_iconPath, _baseIcon), ShowAddApplication, RestartAll, RemoveAllApplications, RunServiceCommand,
				delegate(ServiceInfo service) { OpenFolder(service.LogDirectory); }, RemoveApplication,
				InstallLibrary, RemoveLibrary);
			IntPtr managerHandle = _manager.Handle;

			_showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, delegate(object state, bool timedOut)
			{
				try
				{
					_manager.BeginInvoke((MethodInvoker)delegate { ShowManager(); });
				}
				catch { }
			}, null, System.Threading.Timeout.Infinite, false);

			_menu = new ContextMenuStrip();
			ToolStripMenuItem open = new ToolStripMenuItem("Open ProjectDB Service Manager");
			open.Font = new Font(open.Font, FontStyle.Bold);
			open.Click += delegate { ShowManager(); };
			_menu.Items.Add(open);
			_menu.Items.Add(new ToolStripSeparator());
			ToolStripMenuItem add = new ToolStripMenuItem("Add application...");
			add.Click += delegate { ShowAddApplication(); };
			_menu.Items.Add(add);
			ToolStripMenuItem restartAll = new ToolStripMenuItem("Restart all services");
			restartAll.Click += delegate { RestartAll(); };
			_menu.Items.Add(restartAll);
			ToolStripMenuItem removeAll = new ToolStripMenuItem("Remove all applications...");
			removeAll.Click += delegate { RemoveAllApplications(); };
			_menu.Items.Add(removeAll);
			_menu.Items.Add(new ToolStripSeparator());
			ToolStripMenuItem folder = new ToolStripMenuItem("Open ProjectDB folder");
			folder.Click += delegate { OpenFolder(_baseDirectory); };
			_menu.Items.Add(folder);
			ToolStripMenuItem services = new ToolStripMenuItem("Windows Services...");
			services.Click += delegate { OpenServicesConsole(); };
			_menu.Items.Add(services);
			ToolStripMenuItem uninstall = new ToolStripMenuItem("Uninstall ProjectDB...");
			uninstall.Click += delegate { UninstallProjectDb(); };
			_menu.Items.Add(uninstall);
			_menu.Items.Add(new ToolStripSeparator());
			ToolStripMenuItem exit = new ToolStripMenuItem("Exit ProjectDB Service Manager");
			exit.Click += delegate { ExitTray(); };
			_menu.Items.Add(exit);

			_notifyIcon = new NotifyIcon();
			_notifyIcon.Icon = _iconUnknown;
			_notifyIcon.Text = "ProjectDB Service Manager";
			_notifyIcon.Visible = true;
			_notifyIcon.ContextMenuStrip = _menu;
			_notifyIcon.MouseClick += NotifyIconMouseClick;
			_notifyIcon.DoubleClick += delegate { ShowManager(); };

			_timer = new System.Windows.Forms.Timer();
			_timer.Interval = 2000;
			_timer.Tick += delegate { RefreshServices(true); };
			_timer.Start();

			RefreshServices(false);
			_initialized = true;
		}

		private void NotifyIconMouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
				ShowManager();
		}

		private void ShowManager()
		{
			RefreshServices(false);
			if (!_manager.Visible) _manager.Show();
			if (_manager.WindowState == FormWindowState.Minimized) _manager.WindowState = FormWindowState.Normal;
			_manager.Activate();
			_manager.BringToFront();
		}

		private void ShowAddApplication()
		{
			if (_commandRunning) return;
			if (!File.Exists(_controlHelperPath))
			{
				MessageBox.Show(_manager, "Service control helper was not found. Run ProjectDB Setup to repair this installation.", "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			using (AddApplicationForm form = new AddApplicationForm(_windowIcon))
			{
				if (form.ShowDialog(_manager) != DialogResult.OK) return;
				string host = form.HostValue;
				string appName = form.AppNameValue;
				string password = form.PasswordValue;
				RegisterApplication(host, appName, password);
				password = null;
			}
		}

		private void RegisterApplication(string host, string appName, string password)
		{
			string configDir = Path.Combine(Path.GetTempPath(), "ProjectDB-Manager");
			Directory.CreateDirectory(configDir);
			string configPath = Path.Combine(configDir, "register-" + Guid.NewGuid().ToString("N") + ".json");
			Dictionary<string, string> config = new Dictionary<string, string>();
			config["host"] = host;
			config["appName"] = appName;
			config["password"] = password;
			JavaScriptSerializer serializer = new JavaScriptSerializer();
			File.WriteAllText(configPath, serializer.Serialize(config), new UTF8Encoding(false));
			ProtectManagerTempFile(configPath);
			password = null;

			bool started = RunHelper("add", configPath, "Registering " + appName + "...", delegate(int code)
			{
				try { File.Delete(configPath); } catch { }
				_manager.SetBusy(false, code == 0 ? appName + " was registered successfully." : appName + " could not be registered.");
			});
			if (!started)
			{
				try { File.Delete(configPath); } catch { }
			}
		}


		private void InstallLibrary()
		{
			if (_pendingLibraryOperation != null)
			{
				MessageBox.Show(_manager, "Another library change is already queued.", "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}
			if (!File.Exists(_controlHelperPath))
			{
				MessageBox.Show(_manager, "Service control helper was not found. Run ProjectDB Setup to repair this installation.", "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			using (OpenFileDialog dialog = new OpenFileDialog())
			{
				dialog.Title = _library != null && _library.Installed ? "Replace local library" : "Select local library";
				dialog.Filter = "Shared libraries (*.so)|*.so";
				dialog.DefaultExt = "so";
				dialog.CheckFileExists = true;
				dialog.Multiselect = false;
				if (dialog.ShowDialog(_manager) != DialogResult.OK) return;

				string sourcePath = dialog.FileName;
				if (_library != null && _library.Installed)
				{
					DialogResult confirm = MessageBox.Show(_manager,
						"Replace the current local library with:\r\n\r\n" + sourcePath + "?\r\n\r\nActive ProjectDB services will be restarted.",
						"Replace local library", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
					if (confirm != DialogResult.Yes) return;
				}

				string configDir = Path.Combine(Path.GetTempPath(), "ProjectDB-Library");
				Directory.CreateDirectory(configDir);
				string configPath = Path.Combine(configDir, Guid.NewGuid().ToString("N") + ".json");
				Dictionary<string, string> config = new Dictionary<string, string>();
				config["sourcePath"] = sourcePath;
				JavaScriptSerializer serializer = new JavaScriptSerializer();
				File.WriteAllText(configPath, serializer.Serialize(config), new UTF8Encoding(false));
				ProtectManagerTempFile(configPath);

				PendingLibraryOperation operation = new PendingLibraryOperation();
				operation.Command = "install-library";
				operation.Value = configPath;
				operation.BusyText = "Installing local library and restarting active services...";
				operation.ExpectedSha = ComputeSha256(sourcePath);
				operation.ExpectRemoved = false;
				operation.Completed = delegate(int code)
				{
					try { File.Delete(configPath); } catch { }
					_manager.SetLibraryBusy(false, code == 0 ? "Local library was installed successfully." : "Local library could not be installed.");
				};
				QueueOrRunLibraryOperation(operation);
			}
		}

		private static void ProtectManagerTempFile(string path)
		{
			try
			{
				FileSecurity security = new FileSecurity();
				security.SetAccessRuleProtection(true, false);
				WindowsIdentity identity = WindowsIdentity.GetCurrent();
				security.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, AccessControlType.Allow));
				security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
				security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
				File.SetAccessControl(path, security);
			}
			catch { }
		}

		private void RemoveLibrary()
		{
			if (_pendingLibraryOperation != null || _library == null || !_library.Installed) return;
			DialogResult confirm = MessageBox.Show(_manager,
				"Remove the local application library?\r\n\r\nProjectDB will return to automatic release/cache selection. Active ProjectDB services will be restarted.",
				"Remove local library", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if (confirm != DialogResult.Yes) return;

			PendingLibraryOperation operation = new PendingLibraryOperation();
			operation.Command = "remove-library";
			operation.Value = null;
			operation.BusyText = "Removing local library and restarting services...";
			operation.ExpectedSha = null;
			operation.ExpectRemoved = true;
			operation.Completed = delegate(int code)
			{
				_manager.SetLibraryBusy(false, code == 0 ? "Local library was removed." : "Local library could not be removed.");
			};
			QueueOrRunLibraryOperation(operation);
		}

		private void RemoveAllApplications()
		{
			if (_commandRunning || _services.Count == 0) return;
			DialogResult confirm = MessageBox.Show(_manager,
				"Remove ALL " + _services.Count + " registered ProjectDB application" + (_services.Count == 1 ? "" : "s") + "?\r\n\r\nThis deletes their Windows services, cli.json files, service configuration and service logs. Shared ProjectDB binaries and the Service Manager remain installed.",
				"Remove all ProjectDB applications", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if (confirm != DialogResult.Yes) return;
			RunHelper("remove-all", null, "Removing all registered applications...", delegate(int code)
			{
				_manager.SetBusy(false, code == 0 ? "All registered applications were removed." : "Some applications could not be removed.");
			});
		}

		private void RestartAll()
		{
			if (_commandRunning) return;
			List<string> ids = new List<string>();
			foreach (ServiceInfo item in _services)
				if (item.Installed && item.Status == ServiceControllerStatus.Running) ids.Add(item.ServiceId);
			if (ids.Count == 0) return;
			RunDirectServiceControl(ids, "restart", "Restarting all active services...", "All active services were restarted.");
		}

		private void RunServiceCommand(ServiceInfo service, string command)
		{
			if (service == null || String.IsNullOrWhiteSpace(service.ServiceId)) return;
			RunDirectServiceControl(new List<string> { service.ServiceId }, command,
				command.Substring(0, 1).ToUpperInvariant() + command.Substring(1) + " " + service.AppName + "...",
				service.AppName + ": command completed.");
		}

		private void RunDirectServiceControl(List<string> serviceIds, string command, string busyText, string successText)
		{
			// Administrative helper operations remain exclusive, but ordinary service control does not lock the Manager.
			if (_commandRunning) return;
			_manager.SetBusy(false, busyText);
			ThreadPool.QueueUserWorkItem(delegate
			{
				Exception failure = null;
				try
				{
					foreach (string serviceId in serviceIds) DirectControlService(serviceId, command);
				}
				catch (Exception ex) { failure = ex; }
				try
				{
					_manager.BeginInvoke((MethodInvoker)delegate
					{
						if (failure == null) _manager.SetBusy(false, successText);
						else
						{
							_manager.SetBusy(false, "Service command failed.");
							MessageBox.Show(_manager,
								"Windows denied or could not complete the service control operation.\r\n\r\n" +
								"Run ProjectDB Setup once to repair service control permissions.\r\n\r\n" + failure.Message,
								"ProjectDB Service Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
						}
						RefreshServices(false);
					});
				}
				catch { }
			});
		}

		private static void DirectControlService(string serviceId, string command)
		{
			TimeSpan timeout = TimeSpan.FromSeconds(30);
			using (ServiceController service = new ServiceController(serviceId))
			{
				service.Refresh();
				if (command == "start")
				{
					if (service.Status == ServiceControllerStatus.StopPending) service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
					service.Refresh();
					if (service.Status == ServiceControllerStatus.Stopped) service.Start();
					service.WaitForStatus(ServiceControllerStatus.Running, timeout);
				}
				else if (command == "stop")
				{
					if (service.Status == ServiceControllerStatus.StartPending) service.WaitForStatus(ServiceControllerStatus.Running, timeout);
					service.Refresh();
					if (service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.Paused) service.Stop();
					service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
				}
				else if (command == "restart")
				{
					if (service.Status != ServiceControllerStatus.Stopped)
					{
						if (service.Status == ServiceControllerStatus.StartPending) service.WaitForStatus(ServiceControllerStatus.Running, timeout);
						service.Refresh();
						if (service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.Paused) service.Stop();
						service.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
					}
					service.Start();
					service.WaitForStatus(ServiceControllerStatus.Running, timeout);
				}
				else throw new ArgumentException("Unsupported service action: " + command);
			}
		}

		private void RemoveApplication(ServiceInfo service)
		{
			DialogResult confirm = MessageBox.Show(_manager,
				"Remove application '" + service.AppName + "'?\r\n\r\nThis will delete its Windows service, cli.json, service files and service logs. Shared ProjectDB files and other applications will not be changed.",
				"Remove ProjectDB application", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if (confirm != DialogResult.Yes) return;
			RunHelper("remove", service.AppName, "Removing " + service.AppName + "...", delegate(int code)
			{
				_manager.SetBusy(false, code == 0 ? service.AppName + " was removed." : service.AppName + " could not be removed.");
			});
		}

		private void UninstallProjectDb()
		{
			if (_commandRunning) return;
			string helper = File.Exists(_uninstallHelperPath) ? _uninstallHelperPath : _controlHelperPath;
			if (!VerifyProjectDbBinarySignature(helper, "ProjectDB uninstall component")) return;
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo();
				psi.FileName = helper;
				psi.Arguments = "uninstall-all";
				psi.WorkingDirectory = _baseDirectory;
				psi.UseShellExecute = true;
				psi.Verb = "runas";
				psi.WindowStyle = ProcessWindowStyle.Hidden;
				Process.Start(psi);
			}
			catch (Win32Exception ex)
			{
				if (ex.NativeErrorCode != 1223)
					MessageBox.Show(_manager, ex.Message, "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private bool QueueOrRunLibraryOperation(PendingLibraryOperation operation)
		{
			if (operation == null) return false;
			if (_libraryCommandRunning)
			{
				if (_pendingLibraryOperation != null) return false;
				_pendingLibraryOperation = operation;
				_manager.SetLibraryBusy(true, "Library change queued...");
				return true;
			}
			return StartLibraryOperation(operation);
		}

		private bool StartLibraryOperation(PendingLibraryOperation operation)
		{
			if (operation == null || _libraryCommandRunning) return false;
			if (!File.Exists(_controlHelperPath))
			{
				MessageBox.Show(_manager, "Service control helper was not found: " + _controlHelperPath, "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				if (operation.Completed != null) operation.Completed(-1);
				return false;
			}
			if (!VerifyProjectDbBinarySignature(_controlHelperPath, "ProjectDB service control component"))
			{
				if (operation.Completed != null) operation.Completed(-1);
				return false;
			}
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo();
				psi.FileName = _controlHelperPath;
				psi.Arguments = operation.Command + (String.IsNullOrEmpty(operation.Value) ? String.Empty : " \"" + operation.Value.Replace("\"", "") + "\"");
				psi.WorkingDirectory = _baseDirectory;
				psi.UseShellExecute = true;
				psi.Verb = "runas";
				psi.WindowStyle = ProcessWindowStyle.Hidden;
				Process process = new Process();
				process.StartInfo = psi;
				process.EnableRaisingEvents = true;
				process.Exited += delegate
				{
					int exitCode = process.ExitCode;
					process.Dispose();
					try
					{
						_manager.BeginInvoke((MethodInvoker)delegate
						{
							_libraryCommandRunning = false;
							_libraryExpectedSha = null;
							_libraryExpectRemoved = false;
							if (operation.Completed != null) operation.Completed(exitCode);
							RefreshServices(false);
							PendingLibraryOperation pending = _pendingLibraryOperation;
							_pendingLibraryOperation = null;
							if (pending != null) StartLibraryOperation(pending);
						});
					}
					catch { }
				};
				_libraryExpectedSha = operation.ExpectedSha;
				_libraryExpectRemoved = operation.ExpectRemoved;
				_libraryCommandRunning = true;
				_manager.SetLibraryBusy(true, operation.BusyText);
				process.Start();
				return true;
			}
			catch (Win32Exception ex)
			{
				_libraryCommandRunning = false;
				_libraryExpectedSha = null;
				_libraryExpectRemoved = false;
				_manager.SetLibraryBusy(false, String.Empty);
				if (operation.Completed != null) operation.Completed(-1);
				if (ex.NativeErrorCode != 1223)
					MessageBox.Show(_manager, ex.Message, "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		private bool RunHelper(string command, string value, string busyText, Action<int> completed)
		{
			if (_commandRunning) return false;
			if (!File.Exists(_controlHelperPath))
			{
				MessageBox.Show(_manager, "Service control helper was not found: " + _controlHelperPath, "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
			if (!VerifyProjectDbBinarySignature(_controlHelperPath, "ProjectDB service control component")) return false;
			try
			{
				ProcessStartInfo psi = new ProcessStartInfo();
				psi.FileName = _controlHelperPath;
				psi.Arguments = command + (String.IsNullOrEmpty(value) ? String.Empty : " \"" + value.Replace("\"", "") + "\"");
				psi.WorkingDirectory = _baseDirectory;
				psi.UseShellExecute = true;
				psi.Verb = "runas";
				psi.WindowStyle = ProcessWindowStyle.Hidden;
				Process process = new Process();
				process.StartInfo = psi;
				process.EnableRaisingEvents = true;
				process.Exited += delegate
				{
					int exitCode = process.ExitCode;
					process.Dispose();
					try
					{
						_manager.BeginInvoke((MethodInvoker)delegate
						{
							_commandRunning = false;
							if (completed != null) completed(exitCode);
							RefreshServices(false);
						});
					}
					catch { }
				};
				_commandRunning = true;
				_manager.SetBusy(true, busyText);
				process.Start();
				return true;
			}
			catch (Win32Exception ex)
			{
				_commandRunning = false;
				_manager.SetBusy(false, String.Empty);
				if (ex.NativeErrorCode != 1223)
					MessageBox.Show(_manager, ex.Message, "ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		private bool VerifyProjectDbBinarySignature(string path, string componentName)
		{
			if (!File.Exists(path))
			{
				MessageBox.Show(_manager, componentName + " was not found.\r\n\r\nRun ProjectDB Setup to repair the installation.",
					"ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
			try
			{
				string escaped = path.Replace("'", "''");
				string script =
					"$s=Get-AuthenticodeSignature -LiteralPath '" + escaped + "';" +
					"$subject='';if($null -ne $s.SignerCertificate){$subject=$s.SignerCertificate.Subject};" +
					"[Console]::Write($s.Status.ToString()+'|'+$subject)";
				string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
				ProcessStartInfo psi = new ProcessStartInfo();
				psi.FileName = "powershell.exe";
				psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded;
				psi.UseShellExecute = false;
				psi.CreateNoWindow = true;
				psi.WindowStyle = ProcessWindowStyle.Hidden;
				psi.RedirectStandardOutput = true;
				psi.RedirectStandardError = true;
				using (Process process = Process.Start(psi))
				{
					if (!process.WaitForExit(10000))
					{
						try { process.Kill(); } catch { }
						throw new System.TimeoutException("Signature verification timed out.");
					}
					string output = process.StandardOutput.ReadToEnd().Trim();
					string error = process.StandardError.ReadToEnd().Trim();
					string[] parts = output.Split(new char[] { '|' }, 2);
					bool valid = process.ExitCode == 0 && parts.Length == 2 &&
						String.Equals(parts[0], "Valid", StringComparison.OrdinalIgnoreCase) &&
						parts[1].IndexOf("CN=ProjectDB Local Publisher", StringComparison.OrdinalIgnoreCase) >= 0;
					if (valid) return true;
					string detail = String.IsNullOrWhiteSpace(output) ? error : output;
					MessageBox.Show(_manager,
						"The " + componentName + " has an invalid or missing digital signature.\r\n\r\n" +
						"Run ProjectDB Setup to repair the installation." +
						(String.IsNullOrWhiteSpace(detail) ? String.Empty : "\r\n\r\nSignature status: " + detail),
						"ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return false;
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(_manager,
					"Could not verify the " + componentName + " digital signature.\r\n\r\n" +
					"Run ProjectDB Setup to repair the installation.\r\n\r\n" + ex.Message,
					"ProjectDB", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return false;
			}
		}

		private void RefreshServices(bool notifyChanges)
		{
			List<ServiceInfo> services = DiscoverServices();
			Dictionary<string, string> states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (ServiceInfo service in services)
			{
				RefreshStatus(service);
				RefreshLatestLog(service);
				string state = StateKey(service);
				states[service.ServiceId] = state;
				string oldState;
				if (notifyChanges && _initialized && _lastStates.TryGetValue(service.ServiceId, out oldState) && oldState != state)
					ShowStateChange(service);
			}
			_services = services;
			_library = DiscoverLibrary();
			_lastStates.Clear();
			foreach (KeyValuePair<string, string> pair in states) _lastStates[pair.Key] = pair.Value;
			UpdateTrayIcon();
			_manager.UpdateServices(_services);
			bool libraryControlsAvailable = false;
			if (_libraryCommandRunning)
			{
				if (_libraryExpectRemoved)
					libraryControlsAvailable = _library == null || !_library.Installed;
				else if (!String.IsNullOrWhiteSpace(_libraryExpectedSha) && _library != null && _library.Installed)
					libraryControlsAvailable = String.Equals(_libraryExpectedSha, _library.Sha256, StringComparison.OrdinalIgnoreCase);
			}
			_manager.UpdateLibrary(_library, libraryControlsAvailable);
		}

		private static readonly Regex AnsiEscape = new Regex(@"\x1B\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);
		private static readonly Regex ControlCharacters = new Regex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", RegexOptions.Compiled);
		private static readonly Regex RepeatedSpaces = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);

		private static void RefreshLatestLog(ServiceInfo service)
		{
			service.LogTimestamp = null;
			service.LogPid = null;
			service.LogSource = null;
			service.LogMessage = null;
			service.LogFile = null;
			if (String.IsNullOrWhiteSpace(service.LogDirectory) || !Directory.Exists(service.LogDirectory)) return;

			try
			{
				FileInfo[] files = new DirectoryInfo(service.LogDirectory).GetFiles("*.log", SearchOption.TopDirectoryOnly);
				Array.Sort(files, delegate(FileInfo a, FileInfo b) { return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc); });

				if (TryLoadLatestStructuredLog(files, "out", service)) return;
				TryLoadLatestStructuredLog(files, "err", service);
			}
			catch { }
		}

		private static bool TryLoadLatestStructuredLog(FileInfo[] files, string streamName, ServiceInfo service)
		{
			foreach (FileInfo file in files)
			{
				if (file.Name.IndexOf(streamName, StringComparison.OrdinalIgnoreCase) < 0) continue;
				string[] lines = ReadTailLines(file.FullName);
				for (int i = lines.Length - 1; i >= 0; i--)
				{
					if (String.IsNullOrWhiteSpace(lines[i])) continue;
					ServiceInfo parsed = new ServiceInfo();
					if (!TryParseStructuredLogLine(lines[i], parsed)) continue;
					service.LogFile = file.FullName;
					service.LogTimestamp = parsed.LogTimestamp;
					service.LogPid = parsed.LogPid;
					service.LogSource = parsed.LogSource;
					service.LogMessage = parsed.LogMessage;
					return true;
				}
			}
			return false;
		}

		private static string[] ReadTailLines(string path)
		{
			try
			{
				using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
				{
					if (stream.Length == 0) return new string[0];
					long length = Math.Min(stream.Length, 65536);
					stream.Seek(-length, SeekOrigin.End);
					byte[] buffer = new byte[(int)length];
					int read = 0;
					while (read < buffer.Length)
					{
						int count = stream.Read(buffer, read, buffer.Length - read);
						if (count <= 0) break;
						read += count;
					}
					string text = Encoding.UTF8.GetString(buffer, 0, read);
					return text.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
				}
			}
			catch { return new string[0]; }
		}

		private static bool TryParseStructuredLogLine(string line, ServiceInfo service)
		{
			string clean = CleanConsoleText(line);
			Match match = Regex.Match(clean,
				@"^(?<date>.+?)\s+~\s+(?<pid>\d+)\s+~\s+(?<source>[^~]+?)\s+~\s+(?<message>.*)$",
				RegexOptions.CultureInvariant);
			if (!match.Success) return false;

			string timestamp = NormalizeLogTimestamp(match.Groups["date"].Value.Trim());
			string pid = match.Groups["pid"].Value.Trim();
			string source = match.Groups["source"].Value.Trim();
			string message = match.Groups["message"].Value.Trim();
			int parsedPid;
			if (String.IsNullOrWhiteSpace(timestamp) || !Int32.TryParse(pid, out parsedPid) || parsedPid <= 0 || String.IsNullOrWhiteSpace(source))
				return false;

			service.LogTimestamp = timestamp;
			service.LogPid = pid;
			service.LogSource = source;
			service.LogMessage = message;
			return true;
		}

		private static string NormalizeLogTimestamp(string value)
		{
			if (String.IsNullOrWhiteSpace(value)) return null;
			DateTimeOffset dto;
			if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dto))
				return dto.ToString("HH:mm:ss dd.MM.yyyy", CultureInfo.InvariantCulture);
			DateTime date;
			if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
				return date.ToString("HH:mm:ss dd.MM.yyyy", CultureInfo.InvariantCulture);
			return null;
		}

		private static string CleanConsoleText(string value)
		{
			if (String.IsNullOrEmpty(value)) return value;
			string clean = AnsiEscape.Replace(value, String.Empty);
			clean = ControlCharacters.Replace(clean, String.Empty);
			clean = RepeatedSpaces.Replace(clean, " ");
			return clean.Trim();
		}

		private LibraryInfo DiscoverLibrary()
		{
			LibraryInfo info = new LibraryInfo();
			info.Path = _libraryPath;
			if (!File.Exists(_libraryPath))
				return info;
			try
			{
				FileInfo file = new FileInfo(_libraryPath);
				info.Installed = true;
				info.Size = file.Length;
				info.Version = ReadLibraryVersion(_libraryPath);
				if (File.Exists(_libraryMetadataPath))
				{
					string json = File.ReadAllText(_libraryMetadataPath, Encoding.UTF8);
					JavaScriptSerializer serializer = new JavaScriptSerializer();
					Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(json);
					object value;
					if (String.IsNullOrWhiteSpace(info.Version) && data.TryGetValue("version", out value) && value != null) info.Version = Convert.ToString(value);
					if (data.TryGetValue("sha256", out value) && value != null) info.Sha256 = Convert.ToString(value);
					if (data.TryGetValue("sourceFile", out value) && value != null) info.SourceFile = Convert.ToString(value);
					if (data.TryGetValue("sourceUrl", out value) && value != null) info.SourceUrl = Convert.ToString(value);
					if (data.TryGetValue("sourceType", out value) && value != null) info.SourceType = Convert.ToString(value);
					if (data.TryGetValue("installedAtUtc", out value) && value != null)
					{
						DateTime parsed;
						if (DateTime.TryParse(Convert.ToString(value), out parsed)) info.InstalledAtUtc = parsed.ToUniversalTime();
					}
				}
				if (String.IsNullOrWhiteSpace(info.Sha256))
					info.Sha256 = ComputeSha256(_libraryPath);
			}
			catch { }
			return info;
		}

		private static string ReadLibraryVersion(string path)
		{
			try
			{
				byte[] buffer = new byte[256];
				int count;
				using (FileStream stream = File.OpenRead(path)) count = stream.Read(buffer, 0, buffer.Length);
				if (count <= 0) return null;
				string text = Encoding.ASCII.GetString(buffer, 0, count);
				int colon = text.IndexOf(':');
				if (colon <= 0 || colon > 64) return null;
				string version = text.Substring(0, colon).Trim();
				return Regex.IsMatch(version, "^[0-9A-Za-z._+-]{1,64}$") ? version : null;
			}
			catch { return null; }
		}

		private static string ComputeSha256(string path)
		{
			try
			{
				using (SHA256 sha = SHA256.Create())
				using (FileStream stream = File.OpenRead(path))
				{
					byte[] hash = sha.ComputeHash(stream);
					StringBuilder value = new StringBuilder(hash.Length * 2);
					foreach (byte b in hash) value.Append(b.ToString("x2"));
					return value.ToString();
				}
			}
			catch { return null; }
		}

		private List<ServiceInfo> DiscoverServices()
		{
			List<ServiceInfo> result = new List<ServiceInfo>();
			if (!Directory.Exists(_serviceRoot)) return result;
			string[] directories;
			try { directories = Directory.GetDirectories(_serviceRoot); }
			catch { return result; }
			Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
			foreach (string directory in directories)
			{
				string xmlPath = Path.Combine(directory, "projectdb-service.xml");
				if (!File.Exists(xmlPath)) continue;
				try
				{
					XmlDocument doc = new XmlDocument();
					doc.Load(xmlPath);
					string serviceId = NodeText(doc, "/service/id");
					if (String.IsNullOrWhiteSpace(serviceId)) continue;
					string appName = NodeText(doc, "/service/arguments");
					if (String.IsNullOrWhiteSpace(appName)) appName = new DirectoryInfo(directory).Name;
					string displayName = NodeText(doc, "/service/name");
					string logDirectory = NodeText(doc, "/service/logpath");
					if (String.IsNullOrWhiteSpace(logDirectory)) logDirectory = Path.Combine(_baseDirectory, "log", "service", appName);
					ServiceInfo info = new ServiceInfo();
					info.AppName = appName;
					info.ServiceId = serviceId;
					info.DisplayName = String.IsNullOrWhiteSpace(displayName) ? "ProjectDB - " + appName : displayName;
					info.ServiceDirectory = directory;
					info.LogDirectory = Environment.ExpandEnvironmentVariables(logDirectory);
					result.Add(info);
				}
				catch { }
			}
			return result;
		}

		private static string NodeText(XmlDocument doc, string xpath)
		{
			XmlNode node = doc.SelectSingleNode(xpath);
			return node == null ? null : node.InnerText.Trim();
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct ServiceStatusProcess
		{
			public uint ServiceType;
			public uint CurrentState;
			public uint ControlsAccepted;
			public uint Win32ExitCode;
			public uint ServiceSpecificExitCode;
			public uint CheckPoint;
			public uint WaitHint;
			public uint ProcessId;
			public uint ServiceFlags;
		}

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint desiredAccess);

		[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr OpenService(IntPtr serviceManager, string serviceName, uint desiredAccess);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status,
			int bufferSize, out int bytesNeeded);

		[DllImport("advapi32.dll", SetLastError = true)]
		private static extern bool CloseServiceHandle(IntPtr handle);

		private static int QueryServiceProcessId(string serviceName)
		{
			const uint ScManagerConnect = 0x0001;
			const uint ServiceQueryStatus = 0x0004;
			IntPtr manager = IntPtr.Zero;
			IntPtr service = IntPtr.Zero;
			try
			{
				manager = OpenSCManager(null, null, ScManagerConnect);
				if (manager == IntPtr.Zero) return 0;
				service = OpenService(manager, serviceName, ServiceQueryStatus);
				if (service == IntPtr.Zero) return 0;
				ServiceStatusProcess status;
				int needed;
				if (!QueryServiceStatusEx(service, 0, out status, Marshal.SizeOf(typeof(ServiceStatusProcess)), out needed))
					return 0;
				return status.ProcessId > Int32.MaxValue ? 0 : (int)status.ProcessId;
			}
			catch { return 0; }
			finally
			{
				if (service != IntPtr.Zero) CloseServiceHandle(service);
				if (manager != IntPtr.Zero) CloseServiceHandle(manager);
			}
		}

		private static void RefreshStatus(ServiceInfo service)
		{
			service.ProcessId = 0;
			try
			{
				using (ServiceController controller = new ServiceController(service.ServiceId))
				{
					controller.Refresh();
					service.Status = controller.Status;
					service.Installed = true;
				}
				if (service.Status == ServiceControllerStatus.Running || service.Status == ServiceControllerStatus.StartPending ||
					service.Status == ServiceControllerStatus.Paused || service.Status == ServiceControllerStatus.PausePending ||
					service.Status == ServiceControllerStatus.ContinuePending)
					service.ProcessId = QueryServiceProcessId(service.ServiceId);
			}
			catch { service.Status = null; service.Installed = false; service.ProcessId = 0; }
		}

		private void UpdateTrayIcon()
		{
			Icon icon = _iconUnknown;
			string text;
			if (_services.Count == 0)
			{
				text = "ProjectDB: no applications";
			}
			else
			{
				int running = 0;
				bool pending = false;
				bool stopped = false;
				foreach (ServiceInfo service in _services)
				{
					if (service.Installed && service.Status == ServiceControllerStatus.Running) running++;
					else if (IsPending(service.Status)) pending = true;
					else stopped = true;
				}
				text = _services.Count == 1 ? "ProjectDB - " + _services[0].AppName + ": " + StatusText(_services[0]) : "ProjectDB: " + running + "/" + _services.Count + " running";
				if (stopped) icon = _iconStopped;
				else if (pending) icon = _iconPending;
				else icon = _iconRunning;
			}
			_notifyIcon.Icon = icon;
			_notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
		}

		private void ShowStateChange(ServiceInfo service)
		{
			ToolTipIcon icon = service.Status == ServiceControllerStatus.Running ? ToolTipIcon.Info : ToolTipIcon.Warning;
			_notifyIcon.ShowBalloonTip(2500, "ProjectDB - " + service.AppName, "Status: " + StatusText(service), icon);
		}

		private static string StateKey(ServiceInfo service)
		{
			return !service.Installed || !service.Status.HasValue ? "missing" : service.Status.Value.ToString();
		}

		private static bool IsPending(ServiceControllerStatus? status)
		{
			return status == ServiceControllerStatus.StartPending || status == ServiceControllerStatus.StopPending ||
				status == ServiceControllerStatus.PausePending || status == ServiceControllerStatus.ContinuePending;
		}

		private static string StatusText(ServiceInfo service)
		{
			if (!service.Installed || !service.Status.HasValue) return "not installed";
			switch (service.Status.Value)
			{
				case ServiceControllerStatus.Running: return "running";
				case ServiceControllerStatus.Stopped: return "stopped";
				case ServiceControllerStatus.StartPending: return "starting";
				case ServiceControllerStatus.StopPending: return "stopping";
				case ServiceControllerStatus.Paused: return "paused";
				case ServiceControllerStatus.PausePending: return "pausing";
				case ServiceControllerStatus.ContinuePending: return "resuming";
				default: return service.Status.Value.ToString();
			}
		}

		private static void OpenFolder(string path)
		{
			try
			{
				if (!Directory.Exists(path)) Directory.CreateDirectory(path);
				ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"");
				psi.UseShellExecute = true;
				Process.Start(psi);
			}
			catch { }
		}

		private static void OpenServicesConsole()
		{
			try { Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); }
			catch { }
		}

		private void ExitTray()
		{
			_exiting = true;
			_manager.AllowClose();
			_manager.Close();
			ExitThread();
		}

		private static Icon LoadBaseIcon(string baseDirectory, string iconPath)
		{
			try
			{
				if (File.Exists(iconPath)) return new Icon(iconPath, new Size(32, 32));
			}
			catch { }
			string exePath = Path.Combine(baseDirectory, "projectdb.exe");
			try
			{
				if (File.Exists(exePath))
				{
					using (Icon icon = Icon.ExtractAssociatedIcon(exePath)) if (icon != null) return (Icon)icon.Clone();
				}
			}
			catch { }
			return (Icon)SystemIcons.Application.Clone();
		}

		private static Icon LoadWindowIcon(string iconPath, Icon fallback)
		{
			try
			{
				if (File.Exists(iconPath)) return new Icon(iconPath, new Size(16, 16));
			}
			catch { }
			try { return new Icon(fallback, new Size(16, 16)); }
			catch { return (Icon)SystemIcons.Application.Clone(); }
		}

		private static Image LoadHeaderImage(string iconPath, Icon fallback)
		{
			try
			{
				if (File.Exists(iconPath))
				{
					using (Icon icon = new Icon(iconPath, new Size(48, 48))) return icon.ToBitmap();
				}
			}
			catch { }
			return fallback.ToBitmap();
		}

		private static Icon CreateStatusIcon(Icon baseIcon, Color color)
		{
			using (Bitmap bitmap = new Bitmap(32, 32))
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
				graphics.Clear(Color.Transparent);
				graphics.DrawIcon(baseIcon, new Rectangle(1, 1, 30, 30));
				using (Brush border = new SolidBrush(Color.White))
				using (Brush badge = new SolidBrush(color))
				using (Pen outline = new Pen(Color.FromArgb(100, 0, 0, 0), 1))
				{
					graphics.FillEllipse(border, 19, 19, 13, 13);
					graphics.FillEllipse(badge, 21, 21, 9, 9);
					graphics.DrawEllipse(outline, 21, 21, 9, 9);
				}
				return BitmapToIcon(bitmap);
			}
		}

		private static Icon BitmapToIcon(Bitmap bitmap)
		{
			IntPtr handle = bitmap.GetHicon();
			try { using (Icon temporary = Icon.FromHandle(handle)) return (Icon)temporary.Clone(); }
			finally { DestroyIcon(handle); }
		}

		protected override void ExitThreadCore()
		{
			try { if (_showWait != null) _showWait.Unregister(null); } catch { }
			_timer.Stop();
			_timer.Dispose();
			_notifyIcon.Visible = false;
			_notifyIcon.Dispose();
			_menu.Dispose();
			if (!_exiting) { _manager.AllowClose(); _manager.Close(); }
			_manager.Dispose();
			_iconRunning.Dispose();
			_iconStopped.Dispose();
			_iconPending.Dispose();
			_iconUnknown.Dispose();
			_windowIcon.Dispose();
			_baseIcon.Dispose();
			base.ExitThreadCore();
		}

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
		private static extern bool DestroyIcon(IntPtr handle);
	}

	internal static class Program
	{
		private const int SwShow = 5;
		private const int SwRestore = 9;

		[STAThread]
		private static void Main()
		{
			string session = Process.GetCurrentProcess().SessionId.ToString(CultureInfo.InvariantCulture);
			string mutexName = "Local\\ProjectDBServiceManager_" + session;
			string showEventName = "Local\\ProjectDBServiceManager_Show_" + session;
			bool eventCreated;
			using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName, out eventCreated))
			{
				bool createdNew;
				using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
				{
					if (!createdNew)
					{
						try { showEvent.Set(); } catch { }
						try { PostMessage(new IntPtr(0xFFFF), ManagerForm.ExternalShowMessage, IntPtr.Zero, IntPtr.Zero); } catch { }
						ActivateExistingManager();
						return;
					}
					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);
					Application.Run(new TrayApplicationContext(showEvent));
				}
			}
		}

		private static void ActivateExistingManager()
		{
			try
			{
				IntPtr window = FindWindow(null, "ProjectDB Service Manager");
				if (window == IntPtr.Zero) return;
				ShowWindow(window, SwShow);
				ShowWindow(window, SwRestore);
				SetForegroundWindow(window);
			}
			catch { }
		}

		[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
		private static extern IntPtr FindWindow(string className, string windowName);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr window, int command);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern bool SetForegroundWindow(IntPtr window);

		[System.Runtime.InteropServices.DllImport("user32.dll")]
		private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
	}
}

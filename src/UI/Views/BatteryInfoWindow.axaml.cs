using Avalonia.Controls;
using Avalonia.Threading;
using GHelper.Linux.I18n;
using GHelper.Linux.Platform.Linux;

namespace GHelper.Linux.UI.Views;

/// <summary>
/// Battery information window - shows detailed battery health, energy, and hardware data
/// from /sys/class/power_supply/BAT*.
/// </summary>
public partial class BatteryInfoWindow : Window
{
    private readonly string? _batteryDir;
    private readonly DispatcherTimer _refreshTimer;

    public BatteryInfoWindow()
    {
        InitializeComponent();
        _batteryDir = SysfsHelper.FindBattery();

        Labels.LanguageChanged += ApplyLabels;
        ApplyLabels();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += (_, _) => RefreshLive();

        Loaded += (_, _) =>
        {
            RefreshStatic();
            RefreshLive();
            _refreshTimer.Start();
        };

        Closing += (_, _) => _refreshTimer.Stop();
    }

    private void ApplyLabels()
    {
        Title = Labels.Get("battery_info_title");
        headerHealth.Text = Labels.Get("health_header");
        labelHealthLabel.Text = Labels.Get("health");
        labelCycleCountLabel.Text = Labels.Get("cycle_count");
        labelStatusLabel.Text = Labels.Get("status");
        labelCapLevelLabel.Text = Labels.Get("capacity_level");
        headerEnergy.Text = Labels.Get("energy_header");
        labelRemainingLabel.Text = Labels.Get("remaining");
        labelFullChargeLabel.Text = Labels.Get("full_charge");
        labelDesignCapLabel.Text = Labels.Get("design_capacity");
        labelPowerDrawLabel.Text = Labels.Get("power_draw");
        labelVoltageLabel.Text = Labels.Get("voltage");
        labelTimeEstimateLabel.Text = Labels.Get("time_estimate");
        headerHardware.Text = Labels.Get("hardware_header");
        labelManufacturerLabel.Text = Labels.Get("manufacturer");
        labelModelLabel.Text = Labels.Get("model");
        labelTechnologyLabel.Text = Labels.Get("technology");
        labelDesignVoltageLabel.Text = Labels.Get("design_voltage");
    }

    /// <summary>Read values that don't change while the window is open.</summary>
    private void RefreshStatic()
    {
        if (_batteryDir == null)
        {
            labelHealth.Text = Labels.Get("no_battery");
            return;
        }

        // Manufacturer / model / technology
        labelManufacturer.Text = ReadAttr("manufacturer") ?? Labels.Get("unknown");
        labelModel.Text = ReadAttr("model_name") ?? Labels.Get("unknown");
        labelTechnology.Text = ReadAttr("technology") ?? Labels.Get("unknown");

        // Design voltage
        int vDesign = ReadInt("voltage_min_design");
        labelDesignVoltage.Text = vDesign > 0
            ? $"{vDesign / 1_000_000.0:F3}V"
            : "--";

        // Design capacity
        double energyDesign = ReadEnergyWh("energy_full_design", "charge_full_design");
        labelEnergyDesign.Text = energyDesign > 0
            ? $"{energyDesign:F2} Wh"
            : "--";

        // Cycle count
        int cycles = ReadInt("cycle_count");
        labelCycles.Text = cycles >= 0 ? cycles.ToString() : Labels.Get("n_a");
    }

    /// <summary>Read values that change in real-time.</summary>
    private void RefreshLive()
    {
        if (_batteryDir == null)
            return;

        // Health
        double energyFull = ReadEnergyWh("energy_full", "charge_full");
        double energyDesign = ReadEnergyWh("energy_full_design", "charge_full_design");
        if (energyFull > 0 && energyDesign > 0)
        {
            double health = energyFull * 100.0 / energyDesign;
            labelHealth.Text = $"{health:F1}%  ({energyFull:F2} / {energyDesign:F2} Wh)";
        }

        // Full charge capacity
        labelEnergyFull.Text = energyFull > 0
            ? $"{energyFull:F2} Wh"
            : "--";

        // Energy now
        double energyNow = ReadEnergyWh("energy_now", "charge_now");
        int capacity = ReadInt("capacity");
        if (energyNow > 0)
            labelEnergyNow.Text = $"{energyNow:F2} Wh ({(capacity >= 0 ? $"{capacity}%" : "")})";
        else if (capacity >= 0)
            labelEnergyNow.Text = $"{capacity}%";

        // Status
        labelStatus.Text = ReadAttr("status") ?? "--";

        // Capacity level
        labelCapLevel.Text = ReadAttr("capacity_level") ?? "--";

        // Power draw
        double powerW = ReadEnergyWh("power_now", "current_now");
        string? status = ReadAttr("status");
        if (powerW > 0)
        {
            string dir = status == "Discharging" ? Labels.Get("discharging") : Labels.Get("charging");
            labelPowerDraw.Text = $"{powerW:F1}W ({dir})";
        }
        else
        {
            labelPowerDraw.Text = "0W";
        }

        // Voltage now
        int vNow = ReadInt("voltage_now");
        labelVoltage.Text = vNow > 0
            ? $"{vNow / 1_000_000.0:F3}V"
            : "--";

        labelTimeEstimate.Text = FormatTimeEstimate(status, powerW, energyNow, energyFull);
    }

    /// <summary>
    /// Hours and minutes until the battery is empty, or until it reaches the
    /// configured charge limit. "--" when idle, full, or drawing no measurable
    /// power, since any figure then would be meaningless.
    /// </summary>
    private string FormatTimeEstimate(string? status, double powerW, double energyNow, double energyFull)
    {
        bool charging = status == "Charging";
        if (!charging && status != "Discharging")
            return "--";

        // Below ~0.1W the reading is noise and the quotient explodes.
        if (double.IsNaN(powerW) || powerW < 0.1 || double.IsNaN(energyNow) || energyNow <= 0)
            return "--";

        double remainingWh;
        if (charging)
        {
            if (double.IsNaN(energyFull) || energyFull <= 0)
                return "--";

            // Charging stops at the charge limit, not at 100%.
            int limit = App.Wmi?.GetBatteryChargeLimit() ?? 100;
            if (limit is <= 0 or > 100)
                limit = 100;

            double targetWh = energyFull * (limit / 100.0);
            if (energyNow >= targetWh)
                return "--";
            remainingWh = targetWh - energyNow;
        }
        else
        {
            remainingWh = energyNow;
        }

        double hours = remainingWh / powerW;
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0)
            return "--";

        int totalMinutes = (int)Math.Round(hours * 60.0);
        string dir = charging ? Labels.Get("charging") : Labels.Get("discharging");
        return $"{totalMinutes / 60}h {totalMinutes % 60:D2}m ({dir})";
    }

    // Helpers

    private string? ReadAttr(string name)
    {
        if (_batteryDir == null)
            return null;
        return SysfsHelper.ReadAttribute(Path.Combine(_batteryDir, name))?.Trim();
    }

    private int ReadInt(string name)
    {
        if (_batteryDir == null)
            return -1;
        return SysfsHelper.ReadInt(Path.Combine(_batteryDir, name), -1);
    }

    /// <summary>
    /// Read an energy value in Wh (or power in W), preferring the direct
    /// energy/power attribute and falling back to the charge/current one.
    /// Returns NaN when neither is available, which fails every "> 0" test.
    /// </summary>
    /// <param name="direct">uWh (or uW) attribute, e.g. "energy_full".</param>
    /// <param name="charge">uAh (or uA) attribute, e.g. "charge_full".</param>
    private double ReadEnergyWh(string direct, string charge)
    {
        // Batteries report either energy (uWh) or charge (uAh), never both.
        // uAh * uV / 1e12 gives Wh, and the same factor turns uA * uV into W.
        // Some kernels sign current_now by direction, so magnitude is what counts.
        double d = ReadDouble(direct);
        if (!double.IsNaN(d))
            return Math.Abs(d) / 1_000_000.0;

        double c = ReadDouble(charge);
        double v = ReadDouble("voltage_now");
        if (!double.IsNaN(c) && v > 0)
            return Math.Abs(c) * v / 1e12;

        return double.NaN;
    }

    /// <summary>
    /// Read a sysfs integer as double, so uAh * uV cannot overflow int.
    /// Returns NaN when the attribute is missing or unparseable.
    /// </summary>
    private double ReadDouble(string name)
    {
        if (_batteryDir == null)
            return double.NaN;
        var raw = SysfsHelper.ReadAttribute(Path.Combine(_batteryDir, name))?.Trim();
        if (string.IsNullOrEmpty(raw))
            return double.NaN;
        return double.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }
}

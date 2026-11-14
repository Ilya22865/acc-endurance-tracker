using System.Globalization;
using EnduranceHub.Shared.Dto;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EnduranceHub.Client.ViewModels;

// Калькулятор давления шин и план пит-стопа от споттера.
//
// ACC не даёт удалённо менять пит-стратегию чужой машины, поэтому план — это
// подсказка: споттер собирает её во время гонки, а пилот вводит значения в
// пит-меню (MFD). План уходит на сервер и показывается всем клиентам баннером.
public sealed partial class MainViewModel
{
    private const double MinColdPressure = 19.0;
    private const double MaxColdPressure = 33.0;
    private const double DryHotPressure = 27.7;
    private const double WetHotPressure = 30.0;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private TyreStateDto? _liveTyres;
    private PitPlanDto? _activePitPlan;
    private string _dismissedPitPlanId = "";

    // ------------------------------------------------------------------ живые шины
    [ObservableProperty] private bool _hasTyreData;
    [ObservableProperty] private string _tyreStatusText = "Ожидание данных от агента…";
    [ObservableProperty] private string _tyreFuelText = "—";
    [ObservableProperty] private string _tyreFlText = "—";
    [ObservableProperty] private string _tyreFrText = "—";
    [ObservableProperty] private string _tyreRlText = "—";
    [ObservableProperty] private string _tyreRrText = "—";
    [ObservableProperty] private string _tyreTempFlText = "—";
    [ObservableProperty] private string _tyreTempFrText = "—";
    [ObservableProperty] private string _tyreTempRlText = "—";
    [ObservableProperty] private string _tyreTempRrText = "—";

    // ------------------------------------------------------------------ калькулятор
    [ObservableProperty] private string _calcColdFl = "";
    [ObservableProperty] private string _calcColdFr = "";
    [ObservableProperty] private string _calcColdRl = "";
    [ObservableProperty] private string _calcColdRr = "";
    [ObservableProperty] private string _calcTargetText = "27.7";
    [ObservableProperty] private string _calcOutFl = "—";
    [ObservableProperty] private string _calcOutFr = "—";
    [ObservableProperty] private string _calcOutRl = "—";
    [ObservableProperty] private string _calcOutRr = "—";
    [ObservableProperty] private string _calcDeltaFl = "";
    [ObservableProperty] private string _calcDeltaFr = "";
    [ObservableProperty] private string _calcDeltaRl = "";
    [ObservableProperty] private string _calcDeltaRr = "";
    [ObservableProperty] private double _calcOutFlValue;
    [ObservableProperty] private double _calcOutFrValue;
    [ObservableProperty] private double _calcOutRlValue;
    [ObservableProperty] private double _calcOutRrValue;
    [ObservableProperty] private bool _calcHasResult;
    [ObservableProperty] private string _calcHint = "Введите холодные давления из сетапа — расчёт обновится по живым данным агента.";

    // ------------------------------------------------------------------ план пит-стопа
    [ObservableProperty] private string _pitFuel = "";
    [ObservableProperty] private bool _pitChangeTyres = true;
    [ObservableProperty] private int _pitCompoundIndex;
    [ObservableProperty] private bool _pitRepair;
    [ObservableProperty] private bool _pitDriverSwap;
    [ObservableProperty] private string _pitNote = "";
    [ObservableProperty] private bool _pitUseCalcPressures = true;
    [ObservableProperty] private string _pitStatus = "";
    [ObservableProperty] private string _pitDriverText = "—";

    // ------------------------------------------------------------------ баннер
    [ObservableProperty] private bool _showPitPlan;
    [ObservableProperty] private string _pitPlanTitle = "";
    [ObservableProperty] private string _pitPlanDetails = "";
    [ObservableProperty] private string _pitPlanNote = "";
    [ObservableProperty] private bool _hasPitPlanNote;

    [RelayCommand]
    private void SetDryTarget() => CalcTargetText = DryHotPressure.ToString("0.0", Inv);

    [RelayCommand]
    private void SetWetTarget() => CalcTargetText = WetHotPressure.ToString("0.0", Inv);

    [RelayCommand]
    private void DismissPitPlan()
    {
        _dismissedPitPlanId = _activePitPlan?.Id ?? "";
        ShowPitPlan = false;
    }

    [RelayCommand]
    private async Task SendPitPlanAsync()
    {
        if (_api == null)
        {
            PitStatus = "Нет подключения к серверу";
            return;
        }

        var fuel = ParseNumber(PitFuel) ?? 0;
        var note = (PitNote ?? "").Trim();
        var compound = PitCompoundIndex == 1 ? "Дождь" : "Сухо";

        if (fuel <= 0 && !PitChangeTyres && !PitRepair && !PitDriverSwap && note.Length == 0)
        {
            PitStatus = "План пустой: укажите топливо, шины, ремонт или комментарий";
            return;
        }

        var pressures = PitChangeTyres && PitUseCalcPressures && CalcHasResult
            ? new[] { CalcOutFlValue, CalcOutFrValue, CalcOutRlValue, CalcOutRrValue }
            : Array.Empty<double>();

        var plan = new PitPlanDto
        {
            Driver = CurrentPitDriver,
            FuelLiters = Math.Round(Math.Max(0, fuel), 1),
            ChangeTyres = PitChangeTyres,
            TyreCompound = PitChangeTyres ? compound : "",
            Pressures = pressures,
            Repair = PitRepair,
            DriverSwap = PitDriverSwap,
            Note = note,
        };

        PitStatus = "Отправка…";
        var (saved, error) = await _api.SendPitPlanAsync(plan);
        if (saved == null)
        {
            PitStatus = "Не отправлено: " + error;
            return;
        }

        PitStatus = "План отправлен пилоту";
        ApplyPitPlan(saved);
    }

    [RelayCommand]
    private async Task ClearPitPlanAsync()
    {
        if (_api == null)
        {
            PitStatus = "Нет подключения к серверу";
            return;
        }

        var (ok, error) = await _api.ClearPitPlanAsync(_activePitPlan?.Id);
        PitStatus = ok ? "План снят" : "Не удалось снять: " + error;
        if (ok)
        {
            _activePitPlan = null;
            ShowPitPlan = false;
        }
    }

    /// <summary>Пилот, которому адресуется план: тот, кто сейчас за рулём.</summary>
    public string CurrentPitDriver =>
        !string.IsNullOrWhiteSpace(_lastSnapshot?.CurrentStint?.Driver)
            ? _lastSnapshot!.CurrentStint!.Driver
            : OwnDriverName;

    private void ApplyTyres(TyreStateDto? tyres)
    {
        _liveTyres = tyres;
        PitDriverText = CurrentPitDriver;

        if (tyres == null || tyres.Pressures is not { Length: 4 })
        {
            HasTyreData = false;
            TyreStatusText = "Нет данных: запустите агент на ПК пилота";
            TyreFuelText = "—";
            TyreFlText = TyreFrText = TyreRlText = TyreRrText = "—";
            TyreTempFlText = TyreTempFrText = TyreTempRlText = TyreTempRrText = "—";
            RecalculateTyres();
            return;
        }

        var age = DateTime.UtcNow - tyres.TimestampUtc;
        if (age < TimeSpan.Zero) age = TimeSpan.Zero;
        HasTyreData = age < TimeSpan.FromSeconds(10);
        TyreStatusText = HasTyreData
            ? $"обновлено {age.TotalSeconds:0} с назад"
            : $"данные устарели ({age.TotalSeconds:0} с)";

        TyreFuelText = tyres.FuelLiters.ToString("0.0", Inv) + " л";
        TyreFlText = Psi(tyres.Pressures[0]);
        TyreFrText = Psi(tyres.Pressures[1]);
        TyreRlText = Psi(tyres.Pressures[2]);
        TyreRrText = Psi(tyres.Pressures[3]);

        if (tyres.CoreTemperatures is { Length: 4 })
        {
            TyreTempFlText = Temp(tyres.CoreTemperatures[0]);
            TyreTempFrText = Temp(tyres.CoreTemperatures[1]);
            TyreTempRlText = Temp(tyres.CoreTemperatures[2]);
            TyreTempRrText = Temp(tyres.CoreTemperatures[3]);
        }

        RecalculateTyres();
    }

    private void ApplyPitPlan(PitPlanDto? plan)
    {
        _activePitPlan = plan;
        if (plan == null)
        {
            ShowPitPlan = false;
            return;
        }

        if (string.Equals(plan.Id, _dismissedPitPlanId, StringComparison.Ordinal))
        {
            ShowPitPlan = false;
            return;
        }

        PitPlanTitle = string.IsNullOrWhiteSpace(plan.Driver)
            ? "ПИТ-СТОП"
            : "ПИТ-СТОП · " + plan.Driver;

        var parts = new List<string>();
        if (plan.FuelLiters > 0)
            parts.Add($"топливо +{plan.FuelLiters.ToString("0.#", Inv)} л");
        if (plan.ChangeTyres)
        {
            var compound = string.IsNullOrWhiteSpace(plan.TyreCompound) ? "шины" : "шины: " + plan.TyreCompound;
            parts.Add(compound);
        }
        if (plan.Pressures is { Length: 4 })
            parts.Add("давления " + string.Join(" / ", plan.Pressures.Select(p => p.ToString("0.0", Inv))) + " psi");
        if (plan.Repair) parts.Add("ремонт");
        if (plan.DriverSwap) parts.Add("смена пилота");

        PitPlanDetails = parts.Count > 0 ? string.Join(" · ", parts) : "без изменений";
        PitPlanNote = plan.Note ?? "";
        HasPitPlanNote = PitPlanNote.Length > 0;
        ShowPitPlan = true;
    }

    private void RecalculateTyres()
    {
        var target = ParseNumber(CalcTargetText);
        var cold = new[]
        {
            ParseNumber(CalcColdFl),
            ParseNumber(CalcColdFr),
            ParseNumber(CalcColdRl),
            ParseNumber(CalcColdRr),
        };
        var hot = _liveTyres?.Pressures is { Length: 4 } p ? p : null;

        var values = new double[4];
        var deltas = new string[4];
        var allOk = target.HasValue && hot != null;

        for (int i = 0; i < 4; i++)
        {
            if (cold[i] is double coldNow && target is double targetHot && hot != null &&
                hot[i] is > 1 and < 60)
            {
                var newCold = Math.Clamp(coldNow + (targetHot - hot[i]), MinColdPressure, MaxColdPressure);
                newCold = Math.Round(newCold, 1);
                values[i] = newCold;
                var delta = Math.Round(newCold - coldNow, 1);
                deltas[i] = (delta > 0 ? "+" : "") + delta.ToString("0.0", Inv);
            }
            else
            {
                allOk = false;
            }
        }

        CalcOutFl = values[0] > 0 ? values[0].ToString("0.0", Inv) : "—";
        CalcOutFr = values[1] > 0 ? values[1].ToString("0.0", Inv) : "—";
        CalcOutRl = values[2] > 0 ? values[2].ToString("0.0", Inv) : "—";
        CalcOutRr = values[3] > 0 ? values[3].ToString("0.0", Inv) : "—";
        CalcDeltaFl = deltas[0];
        CalcDeltaFr = deltas[1];
        CalcDeltaRl = deltas[2];
        CalcDeltaRr = deltas[3];
        CalcOutFlValue = values[0];
        CalcOutFrValue = values[1];
        CalcOutRlValue = values[2];
        CalcOutRrValue = values[3];
        CalcHasResult = allOk && values.All(v => v > 0);

        if (hot == null)
            CalcHint = "Ждём живые давления от агента — без них расчёт невозможен.";
        else if (!target.HasValue)
            CalcHint = "Укажите целевое горячее давление (27.7 для сухого асфальта, 30.0 для дождя).";
        else if (!allOk)
            CalcHint = "Заполните холодные давления всех четырёх колёс из текущего сетапа.";
        else
            CalcHint = "Готово: перенесите новые холодные давления в пит-меню.";
    }

    private static double? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, Inv, out var value) ? value : null;
    }

    private static string Psi(float value) => value.ToString("0.0", Inv) + " psi";

    private static string Temp(float value) => value.ToString("0", Inv) + " °C";

    partial void OnCalcColdFlChanged(string value) => RecalculateTyres();
    partial void OnCalcColdFrChanged(string value) => RecalculateTyres();
    partial void OnCalcColdRlChanged(string value) => RecalculateTyres();
    partial void OnCalcColdRrChanged(string value) => RecalculateTyres();
    partial void OnCalcTargetTextChanged(string value) => RecalculateTyres();

    private void ClearPitState()
    {
        _liveTyres = null;
        _activePitPlan = null;
        _dismissedPitPlanId = "";
        HasTyreData = false;
        ShowPitPlan = false;
        TyreStatusText = "Ожидание данных от агента…";
        TyreFuelText = "—";
        TyreFlText = TyreFrText = TyreRlText = TyreRrText = "—";
        TyreTempFlText = TyreTempFrText = TyreTempRlText = TyreTempRrText = "—";
        CalcHasResult = false;
        CalcOutFl = CalcOutFr = CalcOutRl = CalcOutRr = "—";
        CalcDeltaFl = CalcDeltaFr = CalcDeltaRl = CalcDeltaRr = "";
        CalcOutFlValue = CalcOutFrValue = CalcOutRlValue = CalcOutRrValue = 0;
        PitPlanNote = "";
        HasPitPlanNote = false;
        PitStatus = "";
    }
}

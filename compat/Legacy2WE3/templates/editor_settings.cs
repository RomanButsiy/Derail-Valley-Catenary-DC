// Only the upstream electricity billing parameter belongs to this vehicle adapter.
using System;
using UnityModManagerNet;
using static UnityModManagerNet.UnityModManager;
namespace electric_sim.catenary_editor;
public sealed class editor_settings : ModSettings, IDrawable
{
    [Draw("Electricity price, $/kWh (vehicle reload required)")]
    public float PricePerKWh = 10.0f;
    public static float kWh_price { get; private set; } = 10.0f;
    public void OnChange()
    {
        if (float.IsNaN(PricePerKWh) || float.IsInfinity(PricePerKWh) || PricePerKWh <= 0)
            PricePerKWh = 10.0f;
        kWh_price = PricePerKWh;
    }
    public override void Save(ModEntry mod) => Save(this, mod);
    public static void set_up(ModEntry mod)
    {
        editor_settings settings = Load<editor_settings>(mod);
        settings.OnChange();
        mod.OnGUI = entry => settings.Draw(entry);
        mod.OnSaveGUI = settings.Save;
    }
}

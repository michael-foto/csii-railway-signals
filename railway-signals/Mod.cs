using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Serialization;
using RailwaySignals.Systems;

namespace RailwaySignals
{
    public class Mod : IMod
    {
        public static readonly ILog log = LogManager.GetLogger($"{nameof(RailwaySignals)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static Setting setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            setting = new Setting(this);
            setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(setting));
            AssetDatabase.global.LoadSettings(nameof(RailwaySignals), setting, new Setting(this));

            updateSystem.UpdateAt<SignalPrefabSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<SignalNetworkSystem>(SystemUpdatePhase.ModificationEnd);
            updateSystem.UpdateAt<SignalAspectSystem>(SystemUpdatePhase.GameSimulation);
            // The editor never ticks GameSimulation, so without this the aspects never update there.
            updateSystem.UpdateAt<SignalAspectSystem>(SystemUpdatePhase.EditorSimulation);
            // Hides the signal objects from SerializerSystem, which sits between these two.
            updateSystem.UpdateBefore<SignalSaveGuardSystem>(SystemUpdatePhase.Serialize);
            updateSystem.UpdateAfter<SignalSaveGuardSystem>(SystemUpdatePhase.Serialize);
            // Throws away signal objects left in saves written before the guard existed.
            updateSystem.UpdateAfter<PostDeserialize<SignalNetworkSystem>>(SystemUpdatePhase.Deserialize);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (setting != null)
            {
                setting.UnregisterInOptionsUI();
                setting = null;
            }
        }
    }
}

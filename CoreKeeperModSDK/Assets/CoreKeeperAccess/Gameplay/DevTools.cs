using Unity.Collections;
using Unity.Entities;

namespace CoreKeeperAccess.Gameplay
{
    // Flag de l'invincibilite PURE (outil de test, toggle Triangle+F7). Partage entre le
    // mod (qui le bascule) et le systeme serveur (qui force la vie) - meme process en solo.
    // DISTINCT du god mode creatif (F8) : ici le combat reste NORMAL (boss vivant, ennemis
    // actifs, murs solides), le joueur ne meurt juste pas.
    internal static class DevInvincibility
    {
        public static bool Active;
    }

    // Force la vie du joueur au max tant que l'invincibilite de test est active. SERVEUR
    // (autoritaire) : en solo le ServerWorld tourne dans notre process, donc l'ecriture
    // tient (cote client elle serait corrigee au prochain snapshot). Cible les entites
    // HealthCD + PlayerGhost (= les joueurs). Pas de SystemAPI.Query en hot-compile ->
    // GetEntityQuery + EntityManager (cf. fiche acces donnees). health/maxHealth confirmes
    // sur HealthCD ; on rejoue le meme "health = maxHealth" que le jeu pour soigner.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class DevInvincibilitySystem : SystemBase
    {
        private EntityQuery _query;

        protected override void OnCreate()
        {
            _query = GetEntityQuery(
                ComponentType.ReadWrite<HealthCD>(),
                ComponentType.ReadOnly<PlayerGhost>());
            Diag.Log("A11yDevGodMode", "DevInvincibilitySystem cree dans " + World.Name);
        }

        protected override void OnUpdate()
        {
            if (!DevInvincibility.Active) return;
            try
            {
                var ents = _query.ToEntityArray(Allocator.Temp);
                foreach (var e in ents)
                {
                    var h = EntityManager.GetComponentData<HealthCD>(e);
                    if (h.health < h.maxHealth)
                    {
                        h.health = h.maxHealth;
                        EntityManager.SetComponentData(e, h);
                    }
                }
                ents.Dispose();
            }
            catch (System.Exception ex) { Diag.Error("A11yDevGodMode", ex); }
        }
    }
}

using PugTilemap;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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

    // Tampon damier (dev, Triangle+F10) : banc de test tuilage/eclairage. Le mod (client, cf.
    // CoreKeeperAccessMod.TryDevCheckerStamp) pose centre+demiTaille dans CheckerStamp
    // (Gameplay/TileReader.cs) - static partage entre worlds, meme process en solo. LECON DU
    // MEME GENRE QUE DevInvincibilitySystem : une premiere version ecrivait depuis le monde
    // CLIENT (TileReaderSystem) - ecriture silencieusement ecrasee au prochain snapshot NetCode
    // (CreateClientSubMapSystem repackete ClientSubMapLayerCD -> SubMapLayerBuffer client a
    // chaque tick depuis la copie SERVEUR, cf. decompil). Ici : SERVEUR (autoritaire), la seule
    // ecriture qui tient.
    internal static class CheckerStamp
    {
        public static bool Requested;
        public static int2 Center;
        public static int HalfSize;
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class CheckerStampSystem : SystemBase
    {
        protected override void OnCreate()
        {
            Diag.Log("A11yCheckerStamp", "CheckerStampSystem cree dans " + World.Name);
        }

        protected override void OnUpdate()
        {
            if (!CheckerStamp.Requested) return;
            CheckerStamp.Requested = false;
            try
            {
                int h = CheckerStamp.HalfSize;
                int2 c = CheckerStamp.Center;
                var ta = new TileAccessor(ref CheckedStateRef, false);

                int count = 0;
                for (int y = c.y - h; y <= c.y + h; y++)
                {
                    for (int x = c.x - h; x <= c.x + h; x++)
                    {
                        var pos = new int2(x, y);
                        ta.Clear(pos);
                        var tile = new TileCD
                        {
                            tileType = TileType.floor,
                            tileset = (int)(((x + y) & 1) == 0 ? Tileset.BaseBuildingWhite : Tileset.BaseBuildingBlue)
                        };
                        ta.Set(pos, tile);
                        count++;
                    }
                }
                Diag.Log("A11yCheckerStamp", "damier pose (rase+sol) centre=" + c.x + "," + c.y
                    + " demiTaille=" + h + " cases=" + count);
            }
            catch (System.Exception ex) { Diag.Error("A11yCheckerStamp", ex); }
        }
    }
}

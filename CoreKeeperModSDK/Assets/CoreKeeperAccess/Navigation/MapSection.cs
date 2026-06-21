using System.Collections.Generic;
using CoreKeeperAccess.Controls;

namespace CoreKeeperAccess.Navigation
{
    // Une SECTION du menu carte (destinations / points d'interet / mes balises / journal).
    // Refonte 21 juin : chaque section est un objet AUTONOME qui sait se (re)construire,
    // s'annoncer, se naviguer et se confirmer. L'orchestrateur TeleportNavigator ne fait plus
    // que cycler les sections aux bumpers et deleguer -> ajouter une section = une classe, au
    // lieu de toucher les "if (_category == N)" eparpilles dans tout le fichier.
    internal abstract class MapSection
    {
        public abstract string TitleKey { get; }   // libelle de la section (annonce en tete)
        public abstract string EmptyKey { get; }    // libelle quand la section est vide
        public abstract bool IsEmpty { get; }

        // (Re)construit le contenu a partir du scan PARTAGE des marqueurs (les sections de
        // marqueurs filtrent leur part ; le journal l'ignore et recharge ses dialogues).
        public abstract void Rebuild(List<MapMarkerUIElement> scanned);

        public abstract void Enter();        // (re)entre dans la section : reset curseur + annonce 1er
        public abstract void Move(int step);  // D-pad haut/bas
        public abstract void Confirm();       // Croix
        public virtual void Detail() { }      // Triangle + haut (details / relecture)
        public virtual bool HandleDpadLR(int dpadId) => false; // gauche/droite (cascade journal)
        public virtual void Tick() { }        // logique continue (selection differee des balises)
        public virtual void Clear() { }       // a la fermeture de la carte
    }

    // Base des sections qui presentent une LISTE de lignes navigables avec cyclage + earcons de
    // butee (destinations / POI / balises). Le journal n'en herite PAS (nav en cascade propre).
    // Les classes filles fournissent le nombre de lignes, l'annonce, la confirmation et le
    // detail d'une ligne ; le cyclage et le curseur sont geres ici une seule fois.
    internal abstract class MarkerListSection : MapSection
    {
        protected int Index;

        protected abstract int RowCount { get; }
        protected abstract void AnnounceRow(int idx, bool interrupt);
        protected abstract void ConfirmRow(int idx);
        protected virtual void DetailRow(int idx) { }

        public override bool IsEmpty => RowCount == 0;

        public override void Enter()
        {
            Index = 0;
            if (RowCount > 0) AnnounceRow(0, interrupt: false);
        }

        public override void Move(int step)
        {
            int n = RowCount;
            if (n == 0) return;
            int raw = Index + step;
            bool wrapped = raw < 0 || raw >= n;
            Index = (raw + n) % n;
            if (wrapped) UiSfx.Cycle(); else UiSfx.Entry();
            AnnounceRow(Index, interrupt: true);
        }

        public override void Confirm() => ConfirmRow(Index);
        public override void Detail() => DetailRow(Index);
    }
}

using System;

namespace CoreKeeperAccess.Gameplay
{
    // Parseur RIFF/WAV maison (chargement a la volee des sons du mod, aucun asset Unity
    // embarque). Parcourt les chunks GENERIQUEMENT (id + taille, on saute ce qu'on ne
    // connait pas) au lieu de supposer "fmt" suivi direct de "data" - les exports d'outils
    // pro (ElevenLabs & co) inserent souvent un chunk "LIST"/INFO entre les deux. Supporte
    // PCM 16-bit uniquement (mono OU stereo, frequence lue dans le fichier - PAS supposee
    // 44100 : les 3 premiers sons livres sont en 48000 Hz).
    internal static class WavLoader
    {
        public struct Result
        {
            public int Channels;
            public int SampleRate;
            public float[] Samples; // entrelaces si stereo (L,R,L,R,...)
        }

        public static bool TryParse(byte[] raw, string nameForLog, out Result result)
        {
            result = default;
            try
            {
                if (raw == null || raw.Length < 12
                    || raw[0] != 'R' || raw[1] != 'I' || raw[2] != 'F' || raw[3] != 'F'
                    || raw[8] != 'W' || raw[9] != 'A' || raw[10] != 'V' || raw[11] != 'E')
                {
                    Diag.Log("A11yWav", nameForLog + " : en-tete RIFF/WAVE absent ou invalide");
                    return false;
                }

                int pos = 12;
                bool haveFmt = false;
                int channels = 0, sampleRate = 0, bitsPerSample = 0;
                float[] samples = null;

                while (pos + 8 <= raw.Length)
                {
                    string id = "" + (char)raw[pos] + (char)raw[pos + 1] + (char)raw[pos + 2] + (char)raw[pos + 3];
                    uint size = BitConverter.ToUInt32(raw, pos + 4);
                    int dataStart = pos + 8;
                    if (dataStart + size > raw.Length) size = (uint)Math.Max(0, raw.Length - dataStart);

                    if (id == "fmt ")
                    {
                        ushort audioFormat = BitConverter.ToUInt16(raw, dataStart);
                        channels = BitConverter.ToUInt16(raw, dataStart + 2);
                        sampleRate = BitConverter.ToInt32(raw, dataStart + 4);
                        bitsPerSample = BitConverter.ToUInt16(raw, dataStart + 14);
                        if (audioFormat != 1 || bitsPerSample != 16)
                        {
                            Diag.Log("A11yWav", nameForLog + " : format non supporte (audioFormat="
                                + audioFormat + " bits=" + bitsPerSample + "), seul PCM 16-bit est gere");
                            return false;
                        }
                        haveFmt = true;
                    }
                    else if (id == "data")
                    {
                        if (!haveFmt)
                        {
                            Diag.Log("A11yWav", nameForLog + " : chunk data avant chunk fmt, fichier mal forme");
                            return false;
                        }
                        int sampleCount = (int)(size / 2); // 16-bit = 2 octets/echantillon
                        samples = new float[sampleCount];
                        for (int i = 0; i < sampleCount; i++)
                            samples[i] = BitConverter.ToInt16(raw, dataStart + i * 2) / 32768f;
                    }

                    // Chunks RIFF alignes sur un mot (2 octets) : +1 de remplissage si taille impaire.
                    pos = dataStart + (int)size + (int)(size & 1);
                }

                if (!haveFmt || samples == null)
                {
                    Diag.Log("A11yWav", nameForLog + " : chunk fmt ou data manquant");
                    return false;
                }

                result = new Result { Channels = channels, SampleRate = sampleRate, Samples = samples };
                return true;
            }
            catch (Exception ex)
            {
                Diag.Error("A11yWav", ex);
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Threading;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using SFD;
using SFD.Code;
using SFD.Colors;
using SFD.GUI.Text;
using SFD.Parser;
using SFD.Sounds;
using SFD.States;
using SFD.Tiles;
using SFD.UserProgression;
using SFR.Fighter;
using SFR.Helper;
using SFR.Misc;
using static SFD.MenuControls.KeyBindMenuItem;
using Player = SFD.Player;

namespace SFR.Bootstrap;

/// <summary>
/// This is where SFR starts.
/// This class handles and loads all the new textures, music, sounds, tiles, colors etc...
/// This class is also used to tweak some game code on startup, such as window title.
/// </summary>
[HarmonyPatch]
internal static class Assets
{
    private static readonly string _contentPath = Path.Combine(Program.GameDirectory, @"SFR\Content");
    private static readonly string _officialsMapsPath = Path.Combine(_contentPath, @"Data\Maps\Official");

    /// <summary>
    /// Some items like Armband are locked by default.
    /// Here we unlock all items & prevent specific ones from being equipped.
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Challenges), nameof(Challenges.Load))]
    private static IEnumerable<CodeInstruction> UnlockItems(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();

        // Remove the following line:
        // Items.GetItems("FrankenbearSkin", "MechSkin", "HazmatMask", "Armband", "Armband_fem", "OfficerHat", "OfficerJacket", "OfficerJacket_fem", "GermanHelmet", "FLDisguise", "Robe", "Robe_fem").ForEach((Action<Item>) (x => x.Locked = true));
        list.RemoveRange(777, 846 - 777);

        return list;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Constants), nameof(Constants.SetupTextures))]
    private static void LoadAdditionalTeamTextures()
    {
        // Globals.TeamIcon5 = Textures.GetTexture("TeamIcon5");
        // Globals.TeamIcon6 = Textures.GetTexture("TeamIcon6");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(MusicHandler), nameof(MusicHandler.Initialize))]
    private static void LoadMusic()
    {
        Logger.LogInfo("LOADING: Music");
        MusicHandler.m_trackPaths.Add((MusicHandler.MusicTrackID)42, Path.Combine(_contentPath, @"Data\Sounds\Music\Metrolaw.mp3"));
        MusicHandler.m_trackPaths.Add((MusicHandler.MusicTrackID)43, Path.Combine(_contentPath, @"Data\Sounds\Music\FrozenBlood.mp3"));
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(MusicHandler), nameof(MusicHandler.PlayTitleTrack))]
    private static IEnumerable<CodeInstruction> PlayTitleMusic(IEnumerable<CodeInstruction> instructions)
    {
        var musicId = instructions.ElementAt(0);
        musicId.opcode = OpCodes.Ldc_I4_S;
        musicId.operand = 42;
        return instructions;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(SoundHandler), nameof(SoundHandler.Load))]
    private static void LoadSounds(GameSFD game)
    {
        Logger.LogInfo("LOADING: Sounds");
        foreach (string data in Directory.GetFiles(Path.Combine(_contentPath, @"Data\Sounds"), "*.sfds"))
        {
            var soundsData = SFDSimpleReader.Read(data);
            foreach (string soundData in soundsData)
            {
                string[] soundFields = [.. SFDSimpleReader.Interpret(soundData)];
                if (soundFields.Length < 3)
                {
                    continue;
                }

                var sound = new SoundEffect[soundFields.Length - 2];
                float pitch = SFDXParser.ParseFloat(soundFields[1]);

                for (int i = 0; i < sound.Length; i++)
                {
                    string loadPath = Path.Combine(_contentPath, @"Data\Sounds", soundFields[i + 2]);
                    sound[i] = Content.Load<SoundEffect>(loadPath);
                }

                int count = sound.Count(t => t is null);

                if (count > 0)
                {
                    var extraSounds = new SoundEffect[sound.Length - count];
                    int field = 0;
                    foreach (var soundEffect in sound)
                    {
                        if (soundEffect is not null)
                        {
                            extraSounds[field] = soundEffect;
                            field++;
                        }
                    }

                    sound = extraSounds;
                }

                SoundHandler.SoundEffectGroup finalSound = new(soundFields[0], pitch, sound);
                SoundHandler.soundEffects.Add(finalSound);
            }
        }
    }

    /// <summary>
    /// This method is executed whenever we close the game or it crash.
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameSFD), nameof(GameSFD.OnExiting))]
    private static void Dispose() => Logger.LogError("Disposing");

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Constants), nameof(Constants.Load))]
    private static void LoadFonts() => Logger.LogInfo("LOADING: Fonts");

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Constants), nameof(Constants.Load))]
    private static IEnumerable<CodeInstruction> LoadFonts(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is null)
            {
                continue;
            }

            if (instruction.operand.Equals("Data\\Fonts\\"))
            {
                instruction.operand = Path.Combine(_contentPath, @"Data\Fonts");
            }
        }

        return instructions;
    }

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(StateLoading), nameof(StateLoading.Load), [typeof(LoadState)], [ArgumentType.Ref])]
    private static void LoadAdditionTeamIconChat()
    {
        if (TextIcons.m_icons is not null)
        {
            _ = TextIcons.m_icons.Remove("TEAM_5");
            TextIcons.Add("TEAM_5", Textures.GetTexture("TeamIcon5"));
            TextIcons.Add("TEAM_6", Textures.GetTexture("TeamIcon6"));
            TextIcons.Add("TEAM_S", Textures.GetTexture("TeamIconS"));
        }
    }

    /// <summary>
    /// Fix for loading SFR and SFD textures from both paths.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TitleContainer), nameof(TitleContainer.OpenStream))]
    private static bool StreamPatch(string name, ref Stream __result)
    {
        if (name.Contains(@"Content\Data"))
        {
            if (name.EndsWith(".xnb.xnb"))
            {
                name = name.Substring(0, name.Length - 4);
            }

            __result = File.OpenRead(name);
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TileDatabase), nameof(TileDatabase.Load))]
    private static void LoadTiles() => Logger.LogInfo("LOADING: Tiles");

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(TileDatabase), nameof(TileDatabase.Load))]
    private static IEnumerable<CodeInstruction> LoadTiles(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is null)
            {
                continue;
            }

            if (instruction.operand.Equals("Data\\Tiles\\"))
            {
                instruction.operand = Path.Combine(_contentPath, @"Data\Tiles");
            }
            else if (instruction.operand.Equals("Data\\Weapons\\"))
            {
                instruction.operand = Path.Combine(_contentPath, @"Data\Weapons");
                break;
            }
        }

        return instructions;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ColorDatabase), nameof(ColorDatabase.Load))]
    private static bool LoadColors(GameSFD game)
    {
        Logger.LogInfo("LOADING: Colors");
        ColorDatabase.LoadColors(game, Path.Combine(_contentPath, @"Data\Colors\Colors"));
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ColorPaletteDatabase), nameof(ColorDatabase.Load))]
    private static bool LoadColorsPalette(GameSFD game)
    {
        Logger.LogInfo("LOADING: Palettes");
        ColorPaletteDatabase.LoadColorPalettes(game, Path.Combine(_contentPath, @"Data\Colors\Palettes"));
        return false;
    }

    /// <summary>
    /// Load SFR maps into the officials category.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(MapHandler), nameof(MapHandler.ReadMapInfoFromStorages), [])]
    private static bool LoadMaps(ref List<MapInfo> __result)
    {
        Logger.LogInfo("LOADING: Maps");
        Constants.SetThreadCultureInfo(Thread.CurrentThread);
        var list = new List<MapInfo>();
        string[] array =
        [
            Constants.Paths.ContentOfficialMapsPath,
            Constants.Paths.UserDocumentsCustomMapsPath,
            Constants.Paths.UserDocumentsDownloadedMapsPath,
            Constants.Paths.ContentCustomMapsPath,
            Constants.Paths.ContentDownloadedMapsPath,
            _officialsMapsPath
        ];
        var loadedMaps = new HashSet<Guid>();
        foreach (string t in array)
        {
            MapHandler.ReadMapInfoFromStorages(list, t, loadedMaps, true);
        }

        if (!string.IsNullOrEmpty(Constants.STEAM_WORKSHOP_FOLDER))
        {
            MapHandler.ReadMapInfoFromStorages(list, Constants.STEAM_WORKSHOP_FOLDER, loadedMaps, false);
        }

        __result = [.. list.OrderBy(m => m.Name)];
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MapInfo), nameof(MapInfo.SetFilePathData))]
    private static bool LoadMaps(string pathToFile, MapInfo __instance)
    {
        if (string.IsNullOrEmpty(pathToFile))
        {
            __instance.Folder = "Other";
            return false;
        }

        __instance.FullPathToFile = Path.GetFullPath(pathToFile);
        __instance.FileName = Path.GetFileName(__instance.FullPathToFile);
        string directoryName = Path.GetDirectoryName(__instance.FullPathToFile);
        __instance.SaveDate = DateTime.MinValue;
        __instance.IsSteamSubscription = !string.IsNullOrEmpty(Constants.STEAM_WORKSHOP_FOLDER) && pathToFile.StartsWith(Constants.STEAM_WORKSHOP_FOLDER, StringComparison.InvariantCultureIgnoreCase);
        if (directoryName!.StartsWith(_officialsMapsPath, true, null))
        {
            __instance.Folder = "Official" + directoryName.Substring(_officialsMapsPath.Length);
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Textures), nameof(Textures.Load), [])]
    private static void LoadTextures()
    {
        Logger.LogInfo("LOADING: Textures");
        Textures.Load(Path.Combine(_contentPath, @"Data\Images"));
    }

    /// <summary>
    /// Fix for loading SFR and SFD textures from both paths.
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Constants.Paths), nameof(Constants.Paths.GetContentAssetPathFromFullPath))]
    private static bool GetContentAssetPathFromFullPath(string path, ref string __result)
    {
        __result = path;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Items), nameof(Items.Load))]
    private static bool LoadItems(GameSFD game)
    {
        Logger.LogInfo("LOADING: Items");

        // var content = game.Content;
        Items.m_allItems = [];
        Items.m_allFemaleItems = [];
        Items.m_allMaleItems = [];
        Items.m_slotAllItems = new List<Item>[10];
        Items.m_slotFemaleItems = new List<Item>[10];
        Items.m_slotMaleItems = new List<Item>[10];

        for (int i = 0; i < Items.m_slotAllItems.Length; i++)
        {
            Items.m_slotAllItems[i] = [];
            Items.m_slotFemaleItems[i] = [];
            Items.m_slotMaleItems[i] = [];
        }

        var files = Directory.GetFiles(Path.Combine(_contentPath, @"Data\Items"), "*.item", SearchOption.AllDirectories).ToList();
        var originalItems = Directory.GetFiles(Constants.Paths.GetContentFullPath(@"Data\Items"), "*.item", SearchOption.AllDirectories).ToList();
        foreach (string item in originalItems)
        {
            if (files.TrueForAll(f => Path.GetFileNameWithoutExtension(f) != Path.GetFileNameWithoutExtension(item)))
            {
                files.Add(item);
            }
        }

        foreach (string file in files)
        {
            if (GameSFD.Closing)
            {
                return false;
            }

            var item = SFD.Code.Content.Load<Item>(file);
            if (Items.m_allItems.Any(item2 => item2.ID == item.ID))
            {
                throw new("Can't load items");
            }

            item.PostProcess();
            Items.m_allItems.Add(item);
            Items.m_slotAllItems[item.EquipmentLayer].Add(item);
        }

        Items.PostProcessGenders();
        Player.HurtLevel1 = Items.GetItem("HurtLevel1");
        Player.HurtLevel2 = Items.GetItem("HurtLevel2") ?? Player.HurtLevel1;

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(GameSFD), MethodType.Constructor, [])]
    private static void Init(GameSFD __instance) => __instance.Window.Title = $"Superfighters Redux {Globals.SFRVersion}";

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Animations), nameof(Animations.Load))]
    private static bool LoadCustomAnimations(ref bool __result)
    {
        Logger.LogInfo("LOADING: Custom Animations");
        Animations.Data = AnimationsLoadAnimationsDataPipeline(Path.Combine(_contentPath, @"Data\Animations"));
        __result = true;

        return false;
    }

    private static AnimationsData AnimationsLoadAnimationsDataPipeline(string path)
    {
        return AnimationsProcess(AnimationsImport(path));
    }

    private static AnimationsData AnimationsProcess(string input)
    {
        string[] array = input.Split(new char[]
        {
                '|'
        });

        AnimationData[] array2 = new AnimationData[array.Length - 2];
        for (int i = 1; i < array.Length - 1; i++)
        {
            string[] array3 = array[i].Split(new char[]
            {
                    '='
            });
            string name = array3[0];
            string[] array4 = array3[1].Split(new char[]
            {
                    '\n'
            });
            Methods.TrimEnds(ref array4);
            List<AnimationFrameData> list = new List<AnimationFrameData>();
            for (int j = 0; j < array4.Length; j++)
            {
                if (array4[j] == "frame")
                {
                    List<AnimationPartData> animationParts = [];
                    List<AnimationCollisionData> animationCollisions = [];
                    string animationFrameEvent = "";
                    int animationFrameTime = 0;

                    j++;
                    bool flag = false;
                    while (j != array4.Length && !flag)
                    {
                        string[] array5 = array4[j].Split(new char[]
                        {
                                ' '
                        });
                        if (array5[0] == "part")
                        {
                            string animationPartPostFix = "";
                            
                            if (array5.Length > 8)
                            {
                                animationPartPostFix = array5[8];
                            }
                            else
                            {
                                animationPartPostFix = "";
                            }

                            AnimationPartData animationPart = new AnimationPartData(
                                int.Parse(array5[1]),
                                float.Parse(array5[2]),
                                float.Parse(array5[3]),
                                float.Parse(array5[4]),
                                (SpriteEffects)int.Parse(array5[5]),
                                float.Parse(array5[6]),
                                float.Parse(array5[7]),
                                animationPartPostFix
                            );
                            animationParts.Add(animationPart);
                        }
                        else if (array5[0] == "collision")
                        {
                            AnimationCollisionData animationCollision = new AnimationCollisionData(
                                int.Parse(array5[1]),
                                float.Parse(array5[2]),
                                float.Parse(array5[3]),
                                float.Parse(array5[4]),
                                float.Parse(array5[5])
                            );
                            animationCollisions.Add(animationCollision);
                        }
                        else if (array5[0] == "time")
                        {
                            animationFrameTime = int.Parse(array5[1]);
                        }
                        else if (array5[0] == "event")
                        {
                            animationFrameEvent = array5[1];
                        }
                        if (array5[0] != "frame")
                        {
                            j++;
                        }
                        else
                        {
                            flag = true;
                            j--;
                        }
                    }

                    AnimationFrameData animationFrame = new AnimationFrameData(animationParts.ToArray(), animationCollisions.ToArray(), animationFrameEvent, animationFrameTime);
                    list.Add(animationFrame);
                }
            }

            AnimationFrameData[] array6 = new AnimationFrameData[list.Count];
            for (int k = 0; k < array6.Length; k++)
            {
                AnimationPartData[] array7 = new AnimationPartData[list[k].Parts.Count()];
                for (int l = 0; l < array7.Length; l++)
                {
                    array7[l] = new AnimationPartData(list[k].Parts[l].GlobalId, list[k].Parts[l].X, list[k].Parts[l].Y, list[k].Parts[l].Rotation, list[k].Parts[l].Flip, list[k].Parts[l].Scale.X, list[k].Parts[l].Scale.Y, list[k].Parts[l].PostFix);
                }

                AnimationCollisionData[] array8 = new AnimationCollisionData[list[k].Collisions.Count()];
                for (int m = 0; m < array8.Length; m++)
                {
                    array8[m] = new AnimationCollisionData(list[k].Collisions[m].Id, list[k].Collisions[m].X, list[k].Collisions[m].Y, list[k].Collisions[m].Width, list[k].Collisions[m].Height);
                }
                array6[k] = new AnimationFrameData(array7, array8, list[k].Event, list[k].Time);
            }
            array2[i - 1] = new AnimationData(array6, name);
        }

        return new AnimationsData(array2);
    }

    private static string AnimationsImport(string filename)
    {
        string fullPath = Path.GetFullPath(filename);
        string[] array = new string[0];
        array = Directory.GetFiles(fullPath);
        string text = "|";
        for (int i = 0; i < array.Length; i++)
        {
            string text2 = array[i];
            text2 = text2.Substring(text2.LastIndexOf("\\") + 1);
            text2 = text2.Substring(0, text2.LastIndexOf('.'));
            if (text2 != "char_anims")
            {
                text = text + text2 + "=";
                text += File.ReadAllText(array[i]);
                text += '|';
            }
        }
        return text;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Animations), nameof(Animations.Load))]
    private static void EditAnimations()
    {
        var data = Animations.Data;
        var anims = data.Animations;

        var customData = AnimHandler.GetAnimations(data);
        Array.Resize(ref anims, data.Animations.Length + customData.Count);
        for (int i = 0; i < customData.Count; i++)
        {
            anims[anims.Length - 1 - i] = customData[i];
        }

        data.Animations = anims;
        AnimationsData animData = new(data.Animations);
        Animations.Data = animData;
    }
}
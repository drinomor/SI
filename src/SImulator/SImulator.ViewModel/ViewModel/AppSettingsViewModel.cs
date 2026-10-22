using SImulator.ViewModel.Model;
using SImulator.ViewModel.Properties;
using SImulator.ViewModel.PlatformSpecific;
using SIUI.ViewModel;
using SIUI.ViewModel.Core;
using System.Windows.Input;
using System.Xml.Serialization;
using Utils.Commands;

namespace SImulator.ViewModel;

public sealed class AppSettingsViewModel
{
    public AppSettings Model { get; private set; }

    public SettingsViewModel SIUISettings { get; private set; }

    public SoundSettingsViewModel Sounds { get; }

    public GameModes[] Modes { get; } = new GameModes[] { GameModes.Tv, GameModes.Sport };

    /// <summary>
    /// SIGame engine time settings (the same set and limits as in SIGame).
    /// </summary>
    public IReadOnlyList<EngineTimeSettingViewModel> EngineTimes { get; }

    [XmlIgnore]
    public ICommand Reset { get; private set; }

    public AppSettingsViewModel(AppSettings settings)
    {
        Model = settings;
        SIUISettings = new SettingsViewModel(settings.SIUISettings);
        Sounds = new SoundSettingsViewModel(settings.Sounds);
        EngineTimes = CreateEngineTimes();

        Reset = new SimpleCommand(Reset_Executed);
    }

    // Lambdas read Model.EngineTimeSettings every time because the object is replaced on reset
    private EngineTimeSettingViewModel[] CreateEngineTimes() =>
    [
        new(Resources.TimeSettings_ChoosingQuestion, 1, 120, () => Model.EngineTimeSettings.QuestionSelection, v => Model.EngineTimeSettings.QuestionSelection = v),
        new(Resources.TimeSettings_ThinkingOnQuestion, 1, 120, () => Model.EngineTimeSettings.ButtonPressing, v => Model.EngineTimeSettings.ButtonPressing = v),
        new(Resources.TimeSettings_PrintingAnswer, 1, 120, () => Model.EngineTimeSettings.Answering, v => Model.EngineTimeSettings.Answering = v),
        new(Resources.TimeSettings_GivingCat, 1, 120, () => Model.EngineTimeSettings.PlayerSelection, v => Model.EngineTimeSettings.PlayerSelection = v),
        new(Resources.TimeSettings_MakingStake, 1, 120, () => Model.EngineTimeSettings.StakeMaking, v => Model.EngineTimeSettings.StakeMaking = v),
        new(Resources.TimeSettings_ThinkingOnSpecial, 1, 120, () => Model.EngineTimeSettings.SoloAnswering, v => Model.EngineTimeSettings.SoloAnswering = v),
        new(Resources.TimeSettings_RightAnswer, 1, 10, () => Model.EngineTimeSettings.Reflection, v => Model.EngineTimeSettings.Reflection = v),
        new(Resources.TimeSettings_Round, 1, 10800, () => Model.EngineTimeSettings.Round, v => Model.EngineTimeSettings.Round = v),
        new(Resources.TimeSettings_ChoosingFinalTheme, 1, 120, () => Model.EngineTimeSettings.ThemeSelection, v => Model.EngineTimeSettings.ThemeSelection = v),
        new(Resources.TimeSettings_FinalThinking, 1, 120, () => Model.EngineTimeSettings.HiddenAnswering, v => Model.EngineTimeSettings.HiddenAnswering = v),
        new(Resources.TimeSettings_ShowmanDecisions, 1, 300, () => Model.EngineTimeSettings.ShowmanDecision, v => Model.EngineTimeSettings.ShowmanDecision = v),
        new(Resources.TimeSettings_ImageTime, 1, 20, () => Model.EngineTimeSettings.Image, v => Model.EngineTimeSettings.Image = v),
        new(Resources.TimeSettings_PartialImageTime, 1, 20, () => Model.EngineTimeSettings.PartialImage, v => Model.EngineTimeSettings.PartialImage = v),
    ];

    internal void Reset_Executed(object? arg)
    {
        var defaultSettings = new AppSettings();
        PlatformManager.Instance.InitSettings(defaultSettings);

        var defaultUISettings = new Settings();
        var currentSettings = SIUISettings;

        var design = arg == null || arg.ToString() == "1";
        var rules = arg == null || arg.ToString() == "2";
        var buttons = arg == null || arg.ToString() == "4";
        var sounds = arg == null || arg.ToString() == "8";

        if (design)
        {
            currentSettings.QuestionLineSpacing = defaultUISettings.QuestionLineSpacing;
            currentSettings.Model.TableColorString = defaultUISettings.TableColorString;
            currentSettings.Model.TableBackColorString = defaultUISettings.TableBackColorString;
            currentSettings.Model.TableGridColorString = defaultUISettings.TableGridColorString;
            currentSettings.Model.AnswererColorString = defaultUISettings.AnswererColorString;
            currentSettings.TableFontFamily = defaultUISettings.TableFontFamily;
            Model.VideoUrl = defaultSettings.VideoUrl;
            currentSettings.Model.LogoUri = defaultUISettings.LogoUri;
            currentSettings.Model.BackgroundImageUri = defaultUISettings.BackgroundImageUri;
            currentSettings.Model.BackgroundVideoUri = defaultUISettings.BackgroundVideoUri;
            Model.ShowRight = defaultSettings.ShowRight;
            Model.ShowPlayers = defaultSettings.ShowPlayers;
            Model.ShowTableCaption= defaultSettings.ShowTableCaption;
            Model.OralText = defaultSettings.OralText;
        }

        if (rules)
        {            
            Model.GameMode = defaultSettings.GameMode;

            Model.RoundTime = defaultSettings.RoundTime;
            Model.ThinkingTime = defaultSettings.ThinkingTime;
            Model.ThinkingTime2 = defaultSettings.ThinkingTime2;
            Model.SpecialQuestionThinkingTime = defaultSettings.SpecialQuestionThinkingTime;
            Model.FinalQuestionThinkingTime = defaultSettings.FinalQuestionThinkingTime;
            Model.QuestionReadingSpeed = defaultSettings.QuestionReadingSpeed;
            
            Model.BlockingTime = defaultSettings.BlockingTime;
            Model.PlayersView = defaultSettings.PlayersView;

            Model.DropStatsOnBack = defaultSettings.DropStatsOnBack;
            Model.FalseStart = defaultSettings.FalseStart;
            Model.EndQuestionOnRightAnswer = defaultSettings.EndQuestionOnRightAnswer;
            Model.SignalsAfterTimer = defaultSettings.SignalsAfterTimer;
            Model.UsePlayersKeys = defaultSettings.UsePlayersKeys;
            Model.SubstractOnWrong = defaultSettings.SubstractOnWrong;
            Model.PlaySpecials = defaultSettings.PlaySpecials;
            Model.FalseStartMultimedia = defaultSettings.FalseStartMultimedia;
            Model.UseSIGameEngine = defaultSettings.UseSIGameEngine;
            Model.AutoHosting = defaultSettings.AutoHosting;
            Model.PartialImages = defaultSettings.PartialImages;
            Model.PlayAllQuestionsInFinalRound = defaultSettings.PlayAllQuestionsInFinalRound;
            Model.AllowEveryoneToPlayHiddenStakes = defaultSettings.AllowEveryoneToPlayHiddenStakes;
            Model.EngineTimeSettings = defaultSettings.EngineTimeSettings;

            foreach (var engineTime in EngineTimes)
            {
                engineTime.Refresh();
            }

            Model.SaveLogs = defaultSettings.SaveLogs;
            Model.AutomaticGame = defaultSettings.AutomaticGame;

            Model.SpecialsAliases.StakeQuestionAlias = defaultSettings.SpecialsAliases.StakeQuestionAlias;
            Model.SpecialsAliases.SecretQuestionAlias = defaultSettings.SpecialsAliases.SecretQuestionAlias;
            Model.SpecialsAliases.NoRiskQuestionAlias = defaultSettings.SpecialsAliases.NoRiskQuestionAlias;
        }

        if (buttons)
        {
            currentSettings.Model.KeyboardControl = defaultUISettings.KeyboardControl;
        }

        if (sounds)
        {
            Model.PlaySounds = defaultSettings.PlaySounds;
            Sounds.Reset(defaultSettings.Sounds);
        }
    }
}

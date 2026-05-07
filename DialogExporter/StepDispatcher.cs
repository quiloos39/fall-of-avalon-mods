namespace DialogExporter;

// Reads each step type out of a .story binary stream. The byte tag dispatches
// to the matching decoder (mirrors StorySerializerType.CreateStep + StoryStep.Read).
//
// Every decoder MUST consume exactly the bytes its writer emitted, otherwise
// the next step in the same chapter starts decoding at the wrong offset and
// the rest of the file is gibberish.
//
// We start with a minimal set covering dialog-bearing types. Any tag we
// haven't ported throws UnknownStepException with the position so we can add
// it. Unknown == we cannot continue; the .story file has no per-step length
// prefix.

public sealed class UnknownStepException : Exception
{
    public byte Type { get; }
    public int Position { get; }
    public UnknownStepException(byte type, int pos)
        : base($"Unhandled step type {type} (0x{type:X2}) at byte {pos}") { Type = type; Position = pos; }
}

public static class StepDispatcher
{
    public static IStepInfo Decode(byte type, StoryStreamReader r)
    {
        // Every step starts with conditions[]
        var conditions = r.ReadConditionInputArray();

        switch (type)
        {
            // --- Dialog-bearing steps ---
            case 172: return DecodeText(r, conditions, hasIcon: false);
            case 173: return DecodeText(r, conditions, hasIcon: true);
            case 31:  return DecodeChoice(r, conditions, ChoiceKind.Normal);
            case 32:  return DecodeChoice(r, conditions, ChoiceKind.Exit);
            case 33:  return DecodeChoice(r, conditions, ChoiceKind.Submenu);
            case 166: return DecodeStoryStartChoice(r, conditions);
            case 16:  return DecodeBookmark(r, conditions);
            case 136: return new RandomTextShowStep { Conditions = conditions };
            case 152: return DecodeTutorialText(r, conditions);

            // --- Other text-bearing steps (player-visible UI) ---
            case 57:  return DecodeFakeInteractionPrompt(r, conditions);
            case 58:  return DecodeFancyPanel(r, conditions);
            case 114: return DecodeOnHoverChoicePreview(r, conditions);
            case 117: return DecodeOpenHouseUnlock(r, conditions);
            case 130: return DecodePlayTutorialVideo(r, conditions);
            case 151: return DecodeShowTutorialGraphic(r, conditions);
            case 159: return DecodeStatDependantChoice(r, conditions);

            default:
                // Fall through to the auto-generated dispatcher: its decoders
                // skip past every other step type by reading the same field
                // sequence the game's writer emitted.
                if (StepDispatcher_Generated.TrySkip(type, r))
                    return new GenericStep($"Step_{type}", conditions);
                throw new UnknownStepException(type, r.Pos);
        }
    }

    // ------------------------------------------------------------- helpers

    private static SaySpeechStep DecodeText(StoryStreamReader r, (int, bool)[] conditions, bool hasIcon)
    {
        // SText.Read():
        //   actorRef, targetActorRef, lookAtOnlyWithHead, text, gestureKey,
        //   audioClip, hasVoice, overrideDuration, cutDurationMilliseconds, emotions[]
        var step = new SaySpeechStep
        {
            Conditions = conditions,
            ActorGuid = r.ReadActorRefGuid(),
            TargetActorGuid = r.ReadActorRefGuid(),
            LookAtOnlyWithHead = r.ReadBool(),
            TextId = r.ReadLightLocString(),
            GestureKey = r.ReadString(),
            AudioFmodGuid = r.ReadFmodGuid(),
            HasVoice = r.ReadBool(),
            OverrideDuration = r.ReadBool(),
            CutDurationMs = r.ReadInt32(),
            Emotions = r.ReadEmotionDataArray(),
        };
        if (hasIcon)
        {
            step.IconGuid = r.ReadShareableSpriteReference().guid;
        }
        return step;
    }

    private static ChoiceStep DecodeChoice(StoryStreamReader r, (int, bool)[] conditions, ChoiceKind kind)
    {
        // SChoice.Read():
        //   choice (RuntimeChoice), audioClip, spanFlag, span (TimeSpans : int),
        //   choiceIcon (ChoiceIcon : BYTE), shouldDisplayExitIcon, shouldDisplayShopIcon, targetChapter (int)
        // NOTE: ChoiceIcon is `enum : byte` — reading 4 bytes here misaligns the
        // cursor and corrupts every step after this one in the chapter. That
        // single bug accounts for ~850 of the failed stories.
        var step = new ChoiceStep
        {
            Conditions = conditions,
            ChoiceKindFlag = kind,
            Choice = r.ReadRuntimeChoice(),
            AudioFmodGuid = r.ReadFmodGuid(),
            SpanFlag = r.ReadString(),
            Span = r.ReadInt32(),                 // TimeSpans (default-int enum)
            ChoiceIcon = r.ReadByte(),            // ChoiceIcon : byte
            ShouldDisplayExitIcon = r.ReadBool(),
            ShouldDisplayShopIcon = r.ReadBool(),
            TargetChapter = r.ReadChapterIndex(),
        };
        if (kind == ChoiceKind.Exit)
        {
            step.HiddenFromPlayer = r.ReadBool();
        }
        return step;
    }

    private static StoryStartChoiceStep DecodeStoryStartChoice(StoryStreamReader r, (int, bool)[] conditions)
    {
        // SStoryStartChoice.Read():
        //   text (LightLocString), span (int enum), spanFlag (string), targetChapter (int)
        return new StoryStartChoiceStep
        {
            Conditions = conditions,
            TextId = r.ReadLightLocString(),
            Span = r.ReadInt32(),
            SpanFlag = r.ReadString(),
            TargetChapter = r.ReadChapterIndex(),
        };
    }

    private static BookmarkStep DecodeBookmark(StoryStreamReader r, (int, bool)[] conditions)
    {
        return new BookmarkStep
        {
            Conditions = conditions,
            Flag = r.ReadString(),
            StorySettings = r.ReadBool(),
            InvolveHero = r.ReadBool(),
            InvolveAI = r.ReadBool(),
        };
    }

    private static GenericStep DecodeLeave(StoryStreamReader r, (int, bool)[] conditions)
    {
        // SLeave has no extra fields beyond the base — verify when porting.
        return new GenericStep("SLeave", conditions);
    }

    private static GenericStep DecodeInvolveHero(StoryStreamReader r, (int, bool)[] conditions)
    {
        // SInvolveHero — TBD; keeping placeholder until we hand-port.
        return new GenericStep("SInvolveHero", conditions);
    }

    private static TutorialTextStep DecodeTutorialText(StoryStreamReader r, (int, bool)[] conditions)
    {
        // SShowTutorialText.Read():  LocString title, LocString text
        // LocString = { string IdOverride; string ID }
        var (titleOverride, titleId) = r.ReadLocString();
        var (textOverride, textId)   = r.ReadLocString();
        return new TutorialTextStep
        {
            Conditions = conditions,
            TitleId = string.IsNullOrEmpty(titleOverride) ? titleId : titleOverride,
            TextId  = string.IsNullOrEmpty(textOverride)  ? textId  : textOverride,
        };
    }

    // SFakeInteractionPrompt.Read():  LocString title, LocString description
    private static FakeInteractionPromptStep DecodeFakeInteractionPrompt(StoryStreamReader r, (int, bool)[] conditions)
    {
        var (titleOverride, titleId) = r.ReadLocString();
        var (textOverride,  textId)  = r.ReadLocString();
        return new FakeInteractionPromptStep
        {
            Conditions = conditions,
            TitleId = string.IsNullOrEmpty(titleOverride) ? titleId : titleOverride,
            TextId  = string.IsNullOrEmpty(textOverride)  ? textId  : textOverride,
        };
    }

    // SFancyPanel.Read():  LightLocString text, RichEnumReference type
    private static FancyPanelStep DecodeFancyPanel(StoryStreamReader r, (int, bool)[] conditions)
    {
        return new FancyPanelStep
        {
            Conditions = conditions,
            TextId    = r.ReadLightLocString(),
            PanelType = r.ReadRichEnumRefStr(),
        };
    }

    // SOnHoverChoicePreview.Read():  LocString proficienciesSetName
    private static OnHoverChoicePreviewStep DecodeOnHoverChoicePreview(StoryStreamReader r, (int, bool)[] conditions)
    {
        var (idOverride, idVal) = r.ReadLocString();
        return new OnHoverChoicePreviewStep
        {
            Conditions = conditions,
            ProficiencySetKey = string.IsNullOrEmpty(idOverride) ? idVal : idOverride,
        };
    }

    // SOpenHouseUnlock.Read():
    //   ItemSpawningData homeKeyItem, ShareableSpriteReference houseSprite,
    //   LightLocString houseName, LightLocString houseDescription,
    //   int price, LocationReference portalLocation,
    //   bool disappearAfterUnlock, StoryBookmark storyOnUnlock
    private static OpenHouseUnlockStep DecodeOpenHouseUnlock(StoryStreamReader r, (int, bool)[] conditions)
    {
        _ = r.ReadItemSpawningData();
        _ = r.ReadShareableSpriteReference();
        var step = new OpenHouseUnlockStep
        {
            Conditions = conditions,
            HouseNameId        = r.ReadLightLocString(),
            HouseDescriptionId = r.ReadLightLocString(),
            Price              = r.ReadInt32(),
        };
        _ = r.ReadLocationReference();
        _ = r.ReadBool();
        _ = r.ReadStoryBookmark();
        return step;
    }

    // SPlayTutorialVideo.Read():  LocString title, LocString text, LoadingHandle handle
    private static PlayTutorialVideoStep DecodePlayTutorialVideo(StoryStreamReader r, (int, bool)[] conditions)
    {
        var (titleOverride, titleId) = r.ReadLocString();
        var (textOverride,  textId)  = r.ReadLocString();
        _ = r.ReadLoadingHandle();
        return new PlayTutorialVideoStep
        {
            Conditions = conditions,
            TitleId = string.IsNullOrEmpty(titleOverride) ? titleId : titleOverride,
            TextId  = string.IsNullOrEmpty(textOverride)  ? textId  : textOverride,
        };
    }

    // SShowTutorialGraphic.Read():  LocString title, LocString text, ShareableSpriteReference handle
    private static ShowTutorialGraphicStep DecodeShowTutorialGraphic(StoryStreamReader r, (int, bool)[] conditions)
    {
        var (titleOverride, titleId) = r.ReadLocString();
        var (textOverride,  textId)  = r.ReadLocString();
        var (spriteGuid, _)          = r.ReadShareableSpriteReference();
        return new ShowTutorialGraphicStep
        {
            Conditions = conditions,
            TitleId    = string.IsNullOrEmpty(titleOverride) ? titleId : titleOverride,
            TextId     = string.IsNullOrEmpty(textOverride)  ? textId  : textOverride,
            SpriteGuid = spriteGuid,
        };
    }

    // SStatDependantChoice.Read():
    //   byte requirementType, RichEnumReference affectedStat, float statValue,
    //   RichEnumReference comparison, float certainSuccessAtValue,
    //   bool isVisibleToPlayer, bool isChanceVisible,
    //   LightLocString overrideLabel, StoryChapter successChapter
    private static StatDependantChoiceStep DecodeStatDependantChoice(StoryStreamReader r, (int, bool)[] conditions)
    {
        return new StatDependantChoiceStep
        {
            Conditions            = conditions,
            RequirementType       = r.ReadByte(),
            AffectedStat          = r.ReadRichEnumRefStr(),
            StatValue             = r.ReadFloat(),
            Comparison            = r.ReadRichEnumRefStr(),
            CertainSuccessAtValue = r.ReadFloat(),
            IsVisibleToPlayer     = r.ReadBool(),
            IsChanceVisible       = r.ReadBool(),
            OverrideLabelId       = r.ReadLightLocString(),
            SuccessChapter        = r.ReadChapterIndex(),
        };
    }
}

public interface IStepInfo
{
    string Kind { get; }
}

public enum ChoiceKind { Normal, Exit, Submenu }

public sealed class GenericStep : IStepInfo
{
    public string Kind { get; }
    public (int condIdx, bool negate)[] Conditions;
    public GenericStep(string kind, (int, bool)[] conditions) { Kind = kind; Conditions = conditions; }
}

public sealed class SaySpeechStep : IStepInfo
{
    public string Kind => "Text";
    public (int condIdx, bool negate)[] Conditions;
    public string ActorGuid;
    public string TargetActorGuid;
    public bool LookAtOnlyWithHead;
    public uint TextId;
    public string GestureKey;
    public byte[] AudioFmodGuid;
    public bool HasVoice;
    public bool OverrideDuration;
    public int CutDurationMs;
    public (double startTime, string emoteKey, int state, int expressionHandler, float roundDuration)[] Emotions;
    public string IconGuid;
}

public sealed class ChoiceStep : IStepInfo
{
    public string Kind => "Choice";
    public (int condIdx, bool negate)[] Conditions;
    public ChoiceKind ChoiceKindFlag;
    public (uint targetChapterIdx, bool isMain, uint textIdx) Choice;
    public byte[] AudioFmodGuid;
    public string SpanFlag;
    public int Span;
    public byte ChoiceIcon;
    public bool ShouldDisplayExitIcon;
    public bool ShouldDisplayShopIcon;
    public int TargetChapter;
    public bool HiddenFromPlayer;
}

public sealed class StoryStartChoiceStep : IStepInfo
{
    public string Kind => "StoryStartChoice";
    public (int condIdx, bool negate)[] Conditions;
    public uint TextId;
    public int Span;
    public string SpanFlag;
    public int TargetChapter;
}

public sealed class BookmarkStep : IStepInfo
{
    public string Kind => "Bookmark";
    public (int condIdx, bool negate)[] Conditions;
    public string Flag;
    public bool StorySettings;
    public bool InvolveHero;
    public bool InvolveAI;
}

public sealed class RandomTextShowStep : IStepInfo
{
    public string Kind => "RandomTextShow";
    public (int condIdx, bool negate)[] Conditions;
}

public sealed class TutorialTextStep : IStepInfo
{
    public string Kind => "TutorialText";
    public (int condIdx, bool negate)[] Conditions;
    public string TitleId;
    public string TextId;
}

public sealed class FakeInteractionPromptStep : IStepInfo
{
    public string Kind => "FakeInteractionPrompt";
    public (int condIdx, bool negate)[] Conditions;
    public string TitleId;   // LocString key
    public string TextId;    // LocString key (description)
}

public sealed class FancyPanelStep : IStepInfo
{
    public string Kind => "FancyPanel";
    public (int condIdx, bool negate)[] Conditions;
    public uint TextId;
    public string PanelType;
}

public sealed class OnHoverChoicePreviewStep : IStepInfo
{
    public string Kind => "OnHoverChoicePreview";
    public (int condIdx, bool negate)[] Conditions;
    public string ProficiencySetKey;  // LocString key for the proficiencies-set name shown on hover
}

public sealed class OpenHouseUnlockStep : IStepInfo
{
    public string Kind => "OpenHouseUnlock";
    public (int condIdx, bool negate)[] Conditions;
    public uint HouseNameId;
    public uint HouseDescriptionId;
    public int  Price;
}

public sealed class PlayTutorialVideoStep : IStepInfo
{
    public string Kind => "PlayTutorialVideo";
    public (int condIdx, bool negate)[] Conditions;
    public string TitleId;
    public string TextId;
}

public sealed class ShowTutorialGraphicStep : IStepInfo
{
    public string Kind => "ShowTutorialGraphic";
    public (int condIdx, bool negate)[] Conditions;
    public string TitleId;
    public string TextId;
    public string SpriteGuid;
}

public sealed class StatDependantChoiceStep : IStepInfo
{
    public string Kind => "StatDependantChoice";
    public (int condIdx, bool negate)[] Conditions;
    public byte   RequirementType;
    public string AffectedStat;
    public float  StatValue;
    public string Comparison;
    public float  CertainSuccessAtValue;
    public bool   IsVisibleToPlayer;
    public bool   IsChanceVisible;
    public uint   OverrideLabelId;
    public int    SuccessChapter;
}

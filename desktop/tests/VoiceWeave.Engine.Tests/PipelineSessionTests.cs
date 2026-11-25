using VoiceWeave.Core;
using VoiceWeave.Engine;
using CoreDictionary = VoiceWeave.Core.Dictionary;

namespace VoiceWeave.Engine.Tests;

public class PipelineSessionTests
{
    private const int Rate = 16000;

    private sealed class ScriptedAsr(Func<int, string> text) : IAsr
    {
        public int Calls;
        public string Transcribe(float[] pcm) => text(Interlocked.Increment(ref Calls));
        public void Dispose() { }
    }

    private sealed class FixedLid(params string[] codes) : ILanguageIdentifier
    {
        private int _i;
        public string Identify(float[] pcm16k) => codes[Math.Min(_i++, codes.Length - 1)];
    }

    private sealed class CountingCleaner(Func<string, string>? f = null) : ICleaner
    {
        public int Calls;
        public readonly List<string> Seen = new();
        public string Clean(string text, Tone tone) { Calls++; Seen.Add(text); return f?.Invoke(text) ?? text; }
        public void Dispose() { }
    }

    private static float[] Tone(double seconds, float amp = 0.3f) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => amp * MathF.Sin(2 * MathF.PI * 220 * i / Rate)).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static void FeedLikeAMic(PipelineSession s, float[] audio)
    {
        for (var i = 0; i < audio.Length; i += Rate / 10) s.Feed(audio[i..Math.Min(audio.Length, i + Rate / 10)]);
    }

    private static DictationPipeline Pipeline(IAsr english, CleanupService cleanup, string dictionary = "",
        IAsr? regional = null, SpeechMode mode = SpeechMode.English, ILanguageIdentifier? lid = null, bool roman = true)
    {
        var router = new SpeechRouter(l => l == Language.English ? english : regional ?? english, () => lid, mode);
        return new DictationPipeline(router, cleanup, CoreDictionary.Parse(dictionary)) { RomanOutput = () => roman };
    }

    [Fact]
    public void EarlySentences_AreRecognisedWhileTheUserIsStillSpeaking()
    {
        var asr = new ScriptedAsr(n => $"Sentence {n}.");
        var session = Pipeline(asr, new CleanupService(new CountingCleaner())).StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Join(Tone(1.5), Silence(1.0), Tone(1.5), Silence(1.0), Tone(1.5)));
        // before Finish: the first two sentences have already gone to the recogniser
        SpinWait.SpinUntil(() => asr.Calls >= 2, TimeSpan.FromSeconds(3));
        Assert.True(asr.Calls >= 2);
        var result = session.Finish();
        Assert.Equal("Sentence 1. Sentence 2. Sentence 3.", result.Final);
    }

    [Fact]
    public void Silence_NoModelCalls_EmptyResult()
    {
        var asr = new ScriptedAsr(_ => "x");
        var session = Pipeline(asr, new CleanupService(new CountingCleaner())).StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Silence(3));
        Assert.Equal("", session.Finish().Final);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public void ABlipTooShortToBeSpeech_IsIgnored()
    {
        var asr = new ScriptedAsr(_ => "x");
        var session = Pipeline(asr, new CleanupService(new CountingCleaner())).StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(0.1));
        Assert.Equal("", session.Finish().Final);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public void DictionaryIsAppliedBeforeCleanup_AndProtectsItsTerms()
    {
        var cleaner = new CountingCleaner(_ => "I'll be near Myapur at 6 pm.");
        var pipeline = Pipeline(new ScriptedAsr(_ => "i'll be near my pur at six pm"), new CleanupService(cleaner), "my pur => Myapur");
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        var result = session.Finish();
        Assert.Equal("i'll be near Myapur at six pm", cleaner.Seen.Single());
        Assert.Equal("I'll be near Myapur at 6 pm.", result.Final);
        Assert.Equal("i'll be near my pur at six pm", result.Raw);
    }

    [Fact]
    public void CleanTranscript_SkipsTheCleanupModel()
    {
        var cleaner = new CountingCleaner(_ => "unused");
        var session = Pipeline(new ScriptedAsr(_ => "Please review the pull request."), new CleanupService(cleaner)).StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Equal("Please review the pull request.", session.Finish().Final);
        Assert.Equal(0, cleaner.Calls);
    }

    [Fact]
    public void RegionalText_SkipsCleanup_AndIsWrittenInEnglishLetters()
    {
        var cleaner = new CountingCleaner();
        var pipeline = Pipeline(new ScriptedAsr(_ => "unused"), new CleanupService(cleaner),
            regional: new ScriptedAsr(_ => "నేను రేపు వెళ్తున్నాను"), mode: SpeechMode.Telugu);
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Equal("nenu repu veltunnaanu", session.Finish().Final);
        Assert.Equal(0, cleaner.Calls);
    }

    [Fact]
    public void RomanOutputOff_KeepsTheNativeScript()
    {
        var pipeline = Pipeline(new ScriptedAsr(_ => "unused"), new CleanupService(new CountingCleaner()),
            regional: new ScriptedAsr(_ => "నేను రేపు"), mode: SpeechMode.Telugu, roman: false);
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Equal("నేను రేపు", session.Finish().Final);
    }

    [Fact]
    public void RegionalDictionaryEntry_AppliesBeforeRomanising()
    {
        var pipeline = Pipeline(new ScriptedAsr(_ => "unused"), new CleanupService(new CountingCleaner()), "హైదరాబాద్ => Hyderabad",
            regional: new ScriptedAsr(_ => "నేను హైదరాబాద్ వెళ్తున్నాను"), mode: SpeechMode.Telugu);
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Equal("nenu Hyderabad veltunnaanu", session.Finish().Final);
    }

    [Fact]
    public void RuleLearnedFromRomanisedText_AppliesAfterRomanising()
    {
        // the user corrected "haidaraabaad" to "Hyderabad" in the typed text; the rule is in English letters
        var pipeline = Pipeline(new ScriptedAsr(_ => "unused"), new CleanupService(new CountingCleaner()), "haidaraabaad => Hyderabad",
            regional: new ScriptedAsr(_ => "నేను హైదరాబాద్ వెళ్తున్నాను"), mode: SpeechMode.Telugu);
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Equal("nenu Hyderabad veltunnaanu", session.Finish().Final);
    }

    [Fact]
    public void AutoMode_RoutesEnglishAndTeluguSentencesSeparately_AndJoinsThem()
    {
        var cleaner = new CountingCleaner();
        // the English recogniser hears the second sentence (really Telugu) as nonsense words
        var pipeline = Pipeline(new ScriptedAsr(n => n == 1 ? "Hello there." : "Aim chesto naura"), new CleanupService(cleaner),
            regional: new ScriptedAsr(_ => "నేను రేపు"), mode: SpeechMode.EnglishTelugu, lid: new FixedLid("en", "te"));
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Join(Tone(1.5), Silence(1.0), Tone(1.5)));
        var result = session.Finish();
        Assert.Equal("Hello there. Nenu repu", result.Final);   // capital after the full stop, like the Android app
    }

    [Fact]
    public void ForceRegional_SendsTheSentenceToTelugu_EvenIfEnglishLooksFine()
    {
        var english = new ScriptedAsr(_ => "Aim just to know");
        var pipeline = Pipeline(english, new CleanupService(new CountingCleaner()),
            regional: new ScriptedAsr(_ => "ఏం చేస్తున్నావ్"), mode: SpeechMode.EnglishTelugu, lid: new FixedLid("en"));
        var session = pipeline.StartSession(VoiceWeave.Engine.Tone.Casual);
        session.ForceRegional();
        FeedLikeAMic(session, Tone(1.5));
        var result = session.Finish();
        Assert.Equal("em chestunnaav", result.Final);
        Assert.Equal(0, english.Calls);
    }

    [Fact]
    public void WaitsForTheModelsBeforeRecognising_ThenCompletes()
    {
        var gate = new TaskCompletionSource();
        var asr = new ScriptedAsr(_ => "Hello there.");
        var session = Pipeline(asr, new CleanupService(new CountingCleaner())).StartSession(VoiceWeave.Engine.Tone.Casual, () => gate.Task);
        FeedLikeAMic(session, Tone(2));
        var finishing = Task.Run(session.Finish);
        Thread.Sleep(300);
        Assert.Equal(0, asr.Calls);
        Assert.False(finishing.IsCompleted);
        gate.SetResult();
        Assert.Equal("Hello there.", finishing.GetAwaiter().GetResult().Final);
    }

    [Fact]
    public void RecognitionFailure_SurfacesFromFinish()
    {
        var session = Pipeline(new ScriptedAsr(_ => throw new InvalidOperationException("boom")), new CleanupService(new CountingCleaner()))
            .StartSession(VoiceWeave.Engine.Tone.Casual);
        FeedLikeAMic(session, Tone(2));
        Assert.Throws<InvalidOperationException>(session.Finish);
    }

    [Fact]
    public void Cancel_StopsFurtherWork()
    {
        var asr = new ScriptedAsr(_ => "x");
        var session = Pipeline(asr, new CleanupService(new CountingCleaner())).StartSession(VoiceWeave.Engine.Tone.Casual);
        session.Cancel();
        FeedLikeAMic(session, Tone(2));
        Thread.Sleep(100);
        Assert.Equal(0, asr.Calls);
    }

    [Fact]
    public void BatchRun_GivesTheSameAnswerAsASession()
    {
        var pipeline = Pipeline(new ScriptedAsr(_ => "Hello there."), new CleanupService(new CountingCleaner()));
        Assert.Equal("Hello there.", pipeline.Run(Tone(2), VoiceWeave.Engine.Tone.Casual).Final);
    }

    [Fact]
    public void TimingsAreReported()
    {
        var pipeline = Pipeline(new ScriptedAsr(_ => { Thread.Sleep(30); return "Hello there."; }), new CleanupService(new CountingCleaner()));
        var r = pipeline.Run(Tone(2), VoiceWeave.Engine.Tone.Casual);
        Assert.True(r.AsrTime >= TimeSpan.FromMilliseconds(25));
    }
}

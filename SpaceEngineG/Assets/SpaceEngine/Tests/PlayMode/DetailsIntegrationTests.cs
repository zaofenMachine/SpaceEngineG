using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceEngine.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace SpaceEngine.Tests.PlayMode
{
    [DefaultExecutionOrder(100)]
    public sealed class SameFrameSubmissionProbe:MonoBehaviour
    {
        public SpaceEngine.Runtime.UniverseApp App;
        public int RequestedFrame=-1,ObservedFrame=-1,ActivePatches,BuiltPatches;
        void LateUpdate()
        {
            if(Time.frameCount!=RequestedFrame)return;
            ObservedFrame=Time.frameCount;ActivePatches=App.Terrains[0].ActivePatchCount;BuiltPatches=App.Terrains[0].BuiltThisFrame;
        }
    }
    public sealed partial class ExplorerIntegrationTests
    {
        [UnityTest] public IEnumerator DetailsSelectsRelatedBodyWithoutTeleportingAndCapturesFlightInput()
        {
            var position=app.Observer;app.ShowHud=true;app.Tools.SelectCurrentBody(5);app.Tools.ToggleDetails();
            Assert.That(app.Tools.CapturesInput,Is.True);var target=app.Tools.Target;
            var parent=SpaceEngine.Runtime.CelestialDetailsPage.RelatedTarget(target,target.Body.ParentIndex);app.Tools.Select(parent);
            Assert.That(app.Observer.Equals(position),Is.True);Assert.That(app.Tools.Target.BodyIndex,Is.EqualTo(target.Body.ParentIndex));
            for(int tab=0;tab<4;tab++){app.Tools.DetailsPage.SelectTab(tab);yield return null;Assert.That(app.Tools.DetailsPage.SelectedTab,Is.EqualTo(tab));}
            app.Tools.ToggleDetails();Assert.That(app.Tools.CapturesInput,Is.False);
        }
        [UnityTest] public IEnumerator VersionTwoFlightFallbackPreservesItsOwnWorldAndOriginalBytes()
        {
            string current=app.CaptureFlightState();app.Settings.WorldGenerationVersion=2;app.LoadSystem(0,0,0);app.FramePlanet(0,123456);
            string legacy=app.CaptureFlightState();var position=app.Observer;double orbit=app.Planet.SemiMajorAxis;
            string oldPath=Path.Combine(saveDirectory,"flight-v2.json");File.WriteAllText(oldPath,legacy);
            Assert.That(app.RestoreFlightState(current),Is.True);Assert.That(app.Settings.WorldGenerationVersion,Is.EqualTo(3));
            app.LoadFlight();Assert.That(app.Settings.WorldGenerationVersion,Is.EqualTo(2));Assert.That(app.System.GenerationVersion,Is.EqualTo(2));
            Assert.That(app.Planet.SemiMajorAxis,Is.EqualTo(orbit));Assert.That(app.Observer.Equals(position),Is.True);Assert.That(File.ReadAllText(oldPath),Is.EqualTo(legacy));
            app.SaveFlight();Assert.That(File.Exists(oldPath+".bak"),Is.True);Assert.That(app.RestoreFlightState(current),Is.True);
            Assert.That(app.SavePath,Does.EndWith("flight-v3.json"));yield return null;
        }
        [UnityTest] public IEnumerator ACoroutineWorldReplacementIsSubmittedBeforeTheSameFramesRender()
        {
            var probe=app.gameObject.AddComponent<SameFrameSubmissionProbe>();probe.App=app;
            try
            {
                yield return null;app.ReturnHome();app.SetAltitude(600000);probe.RequestedFrame=Time.frameCount;
                yield return null;
                Assert.That(probe.ObservedFrame,Is.EqualTo(probe.RequestedFrame));
                Assert.That(probe.ActivePatches,Is.GreaterThan(0),"LateUpdate must submit new procedural terrain after a coroutine changes the world");
                Assert.That(probe.BuiltPatches,Is.GreaterThan(0));
            }
            finally{UnityEngine.Object.Destroy(probe);}
        }
        [UnityTest] public IEnumerator TypingIInSearchDoesNotOpenDetailsOrMoveTheObserver()
        {
            var input=InputSystem.settings;var background=input.backgroundBehavior;var routing=input.editorInputBehaviorInPlayMode;Keyboard keyboard=null;
            try
            {
                input.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;input.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard=InputSystem.AddDevice<Keyboard>();keyboard.MakeCurrent();app.VerificationOwnsFlight=false;app.ShowHud=true;app.Tools.ToggleSearch();yield return null;
                var position=app.Observer;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.I,Key.W));yield return null;yield return null;
                Assert.That(app.Tools.SearchOpen,Is.True);Assert.That(app.Tools.ShowDetails,Is.False);Assert.That(app.Observer.Equals(position),Is.True);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;app.Tools.ClosePanels();
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.I));yield return null;yield return null;Assert.That(app.Tools.ShowDetails,Is.True);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F2));yield return null;yield return null;Assert.That(app.Tools.ShowDetails,Is.False);
            }
            finally
            {
                if(keyboard!=null){InputSystem.ResetDevice(keyboard);InputSystem.RemoveDevice(keyboard);}
                input.backgroundBehavior=background;input.editorInputBehaviorInPlayMode=routing;app.VerificationOwnsFlight=true;
            }
        }
    }
}

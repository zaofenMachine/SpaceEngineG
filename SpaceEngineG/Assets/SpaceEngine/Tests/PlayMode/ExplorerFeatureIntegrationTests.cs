using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using SpaceEngine.Core;
using SpaceEngine.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace SpaceEngine.Tests.PlayMode
{
    public sealed partial class ExplorerIntegrationTests
    {
        [UnityTest] public IEnumerator TargetSelectionAndChineseFiltersRetainTheObserver()
        {
            var tools=app.Tools;yield return null;Assert.That(tools.Target,Is.Not.Null,"Default selection survives LateUpdate without opening search");
            var exact=app.Observer;tools.RefreshCatalogue();Assert.That(tools.CatalogueCount,Is.GreaterThan(app.System.Planets.Length));
            tools.Filter.Query="海洋";tools.Filter.LandableOnly=true;tools.RefreshResults();Assert.That(tools.Results.Count,Is.GreaterThan(0));
            foreach(var target in tools.Results){Assert.That(target.IsStar,Is.False);Assert.That(target.Body.Kind,Is.EqualTo(PlanetKind.Oceanic));}
            tools.Select(tools.Results[0]);Assert.That(app.Observer.Equals(exact),Is.True,"Catalogue selection must not teleport");
            tools.Filter.Query="";tools.Filter.PlanetKind=(int)PlanetKind.GasGiant;tools.Filter.LandableOnly=false;tools.RefreshResults();
            Assert.That(tools.Results.Count,Is.GreaterThan(0));foreach(var target in tools.Results)Assert.That(target.Body.Kind,Is.EqualTo(PlanetKind.GasGiant));
            tools.Filter.LandableOnly=true;tools.RefreshResults();Assert.That(tools.Results.Count,Is.Zero,"Gas giants cannot pass a landable filter");
            tools.SelectCurrentBody(0);yield return null;Assert.That(tools.Target.BodyIndex,Is.Zero);Assert.That(app.Observer.Equals(exact),Is.True);
            Assert.That(tools.AddAddress(long.MaxValue,0,0),Is.False,"An invalid user-entered address must not throw from the UI");
        }

        [UnityTest] public IEnumerator NativeWheelNotchesAndModalTextDoNotFlyTheCamera()
        {
            var input=InputSystem.settings;var background=input.backgroundBehavior;var routing=input.editorInputBehaviorInPlayMode;
            Keyboard keyboard=null;Mouse mouse=null;
            try
            {
                input.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                input.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();keyboard.MakeCurrent();mouse.MakeCurrent();
                app.VerificationOwnsFlight=false;app.TimeRate=0;app.SpeedMultiplier=1;app.ShowHud=true;
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),scroll=new Vector2(0,120)});
                yield return null;yield return null;Assert.That(app.SpeedMultiplier,Is.EqualTo(app.Settings.ScrollSpeedFactor).Within(.0001));
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),scroll=new Vector2(0,1)});
                yield return null;yield return null;Assert.That(app.SpeedMultiplier,Is.EqualTo(app.Settings.ScrollSpeedFactor*app.Settings.ScrollSpeedFactor).Within(.0001));
                Assert.That(FlightSpeedControl.WheelNotches(-120),Is.EqualTo(-1));Assert.That(FlightSpeedControl.WheelNotches(-1),Is.EqualTo(-1));
                app.Tools.ToggleSearch();yield return null;var position=app.Observer;double multiplier=app.SpeedMultiplier;
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.G,Key.M));
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.5f),scroll=new Vector2(0,120)});
                yield return null;yield return null;
                Assert.That(app.Observer.Equals(position),Is.True,"Typing in search must not drive WASD/navigation");Assert.That(app.SpeedMultiplier,Is.EqualTo(multiplier));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.QueueStateEvent(mouse,new MouseState());yield return null;
                app.Tools.ClosePanels();app.VerificationOwnsFlight=true;
            }
            finally
            {
                if(keyboard!=null){InputSystem.ResetDevice(keyboard);InputSystem.RemoveDevice(keyboard);}
                if(mouse!=null){InputSystem.ResetDevice(mouse);InputSystem.RemoveDevice(mouse);}
                input.backgroundBehavior=background;input.editorInputBehaviorInPlayMode=routing;
            }
        }

        [UnityTest] public IEnumerator TargetFlightHandsOffToContinuousLandingWithoutLosingTheHorizon()
        {
            var tools=app.Tools;tools.SelectCurrentBody(0);var before=app.Observer;
            tools.StartTargetFlight();Assert.That(app.Observer.Equals(before),Is.True);
            tools.TickMotion(.15);Assert.That(app.RadialNavigationActive,Is.True);Assert.That(tools.TrackingTarget,Is.False);
            double previous=app.Altitude;
            for(int i=0;i<400&&app.RadialNavigationActive;i++)
            {
                app.AdvanceRadialFlight(.15);app.ConstrainGround();app.SetObserver(app.Observer,app.ViewCamera.transform.rotation);
                Assert.That(app.Altitude,Is.LessThanOrEqualTo(previous+.001));previous=app.Altitude;
            }
            Assert.That(app.Navigation,Does.StartWith("LANDED"));Assert.That(app.Altitude,Is.EqualTo(app.Settings.MinimumClearance).Within(.05));
            Quaternion arrival=app.ViewCamera.transform.rotation;tools.TickMotion(.15);
            Assert.That(Quaternion.Angle(arrival,app.ViewCamera.transform.rotation),Is.LessThan(.01),"Tracking must not turn the landed view down into the planet");
            yield return null;
        }

        [UnityTest] public IEnumerator OrbitalOverlayHasRealPathsAndHonorsDisableAndVisibility()
        {
            app.Tools.FrameSystem();yield return null;yield return null;
            var overlay=app.Tools.Orbits;Assert.That(overlay.VisibleSegmentCount,Is.GreaterThan(100));
            app.Tools.ToggleOrbits();yield return null;Assert.That(overlay.VisibleSegmentCount,Is.Zero);
            app.Tools.ToggleOrbits();overlay.enabled=false;yield return null;Assert.That(overlay.GetComponent<MeshRenderer>().enabled,Is.False);
            overlay.enabled=true;yield return null;Assert.That(overlay.VisibleSegmentCount,Is.GreaterThan(100));
        }

        [UnityTest] public IEnumerator ViewBookmarksRoundTripFullPrecisionAndKeepAnAtomicBackup()
        {
            var tools=app.Tools;string folder=Path.Combine(Application.temporaryCachePath,"SpaceEngineBookmarkTest-"+Guid.NewGuid().ToString("N"));
            string testPath=Path.Combine(folder,"bookmarks.json");var scope=tools.UseTemporaryBookmarkStore(testPath);
            try
            {
                app.SetAltitude(123456,true);app.SimulationTime=12345;var exact=app.Observer;Quaternion rotation=app.ViewCamera.transform.rotation;
                int index=tools.Bookmarks.Count;tools.AddBookmark("测试位置");tools.AddBookmark("备份检查");
                Assert.That(File.Exists(tools.BookmarkPath),Is.True);Assert.That(File.Exists(tools.BookmarkPath+".bak"),Is.True);
                app.SetAltitude(222222);Assert.That(tools.RestoreBookmark(index),Is.True);
                Assert.That(app.Observer.Equals(exact),Is.True,"Full precision restore delta: "+app.Observer.RelativeTo(exact));Assert.That(app.SimulationTime,Is.EqualTo(12345));
                Assert.That(Quaternion.Angle(rotation,app.ViewCamera.transform.rotation),Is.LessThan(.01));
                tools.RemoveBookmark(index+1);tools.RemoveBookmark(index);
            }
            finally
            {
                foreach(string path in new[]{testPath,testPath+".bak",testPath+".tmp"})if(File.Exists(path))File.Delete(path);
                if(Directory.Exists(folder))Directory.Delete(folder);
                scope.Dispose();
            }
            yield return null;
        }
    }
}

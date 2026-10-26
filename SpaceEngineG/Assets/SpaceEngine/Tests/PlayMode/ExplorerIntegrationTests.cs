using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceEngine.Core;
using SpaceEngine.Runtime;
using SpaceEngine.Terrain;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SpaceEngine.Tests.PlayMode
{
    /// <summary>
    /// Short engine integration checks. The separate rendered acceptance route owns
    /// the expensive full interstellar descent, screenshots and frame timing.
    /// </summary>
    public sealed class ExplorerIntegrationTests
    {
        UniverseApp app;
        UniverseSettings settings;
        Scene previousScene, testScene;
        int previousVsync, previousTargetFrameRate;
        bool previousFog;
        Color previousAmbient;
        Material previousSkybox;
        string savePath;
        readonly Dictionary<string, byte[]> previousSaveFiles = new Dictionary<string, byte[]>();
        readonly List<UniverseApp> suspendedApps = new List<UniverseApp>();
        readonly List<Camera> suspendedCameras = new List<Camera>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousScene = SceneManager.GetActiveScene();
            previousVsync = QualitySettings.vSyncCount; previousTargetFrameRate = Application.targetFrameRate;
            previousFog = RenderSettings.fog; previousAmbient = RenderSettings.ambientLight; previousSkybox = RenderSettings.skybox;
            savePath = Path.Combine(Application.persistentDataPath, "flight.json");
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp" })
                previousSaveFiles[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;

            // Unity Test Runner normally supplies an empty scene. If an explorer
            // is already open, suspend its simulation and camera for this scope.
            foreach (var existing in Object.FindObjectsByType<UniverseApp>())
            {
                if (existing.enabled) { suspendedApps.Add(existing); existing.enabled = false; }
                if (existing.ViewCamera != null && existing.ViewCamera.enabled)
                { suspendedCameras.Add(existing.ViewCamera); existing.ViewCamera.enabled = false; }
            }
            testScene = SceneManager.CreateScene("SpaceEngine PlayMode " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(testScene);
            var resource = Resources.Load<UniverseSettings>("UniverseSettings");
            Assert.That(resource, Is.Not.Null, "Run Space Engine/Setup Explorer Scene to create the shipped Resources settings.");
            settings = Object.Instantiate(resource);
            settings.name = "PlayMode temporary settings";
            settings.StarCellRadius = 2;
            settings.TerrainResolution = 6;
            settings.TerrainPatchBudget = 48;
            settings.TerrainMaxLevel = 18;
            settings.MeshBuildsPerFrame = 2;
            settings.MeshBuildBudgetMs = 2;
            var root = new GameObject("PlayMode explorer under test");
            root.SetActive(false);
            app = root.AddComponent<UniverseApp>();
            app.Settings = settings;
            app.VerificationOwnsFlight = true;
            app.TimeRate = 0;
            root.SetActive(true);
            app.ShowHud = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Restore the user's save byte-for-byte, even when an assertion fails.
            foreach (var snapshot in previousSaveFiles)
            {
                if (snapshot.Value != null) File.WriteAllBytes(snapshot.Key, snapshot.Value);
                else if (File.Exists(snapshot.Key)) File.Delete(snapshot.Key);
            }
            previousSaveFiles.Clear();
            if (app != null) Object.Destroy(app.gameObject);
            yield return null;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(testScene);
                if (unload != null) yield return unload;
            }
            if (settings != null) Object.Destroy(settings);
            foreach (var existing in suspendedApps) if (existing != null) existing.enabled = true;
            foreach (var camera in suspendedCameras) if (camera != null) camera.enabled = true;
            suspendedApps.Clear(); suspendedCameras.Clear();
            QualitySettings.vSyncCount = previousVsync; Application.targetFrameRate = previousTargetFrameRate;
            RenderSettings.skybox = previousSkybox; RenderSettings.fog = previousFog; RenderSettings.ambientLight = previousAmbient;
            yield return null;
        }

        [UnityTest]
        public IEnumerator StartupCreatesSeededBodiesAndVisibleProceduralTerrain()
        {
            var expected = UniverseGenerator.GenerateSystem(settings.UniverseSeed, 0, 0, 0);
            Assert.That(app.System.Seed, Is.EqualTo(expected.Seed));
            Assert.That(app.System.Planets[0].Seed, Is.EqualTo(expected.Planets[0].Seed));
            Assert.That(app.Terrains.Length, Is.EqualTo(expected.Planets.Length));
            Assert.That(app.Stars.Count, Is.EqualTo(125));
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(RenderSettings.skybox, Is.Null);
            for (int i = 0; i < 60 && app.Terrains[0].ActivePatchCount == 0; i++) yield return null;
            PlanetTerrain terrain = app.Terrains[0];
            Assert.That(terrain.ActivePatchCount, Is.GreaterThan(0), "The budgeted mesher must produce renderable coverage.");
            Assert.That(terrain.TriangleCount, Is.GreaterThan(48));
            bool visibleMesh = false;
            foreach (var renderer in terrain.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || mesh.vertexCount <= 20) continue;
                Assert.That(mesh.normals.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(mesh.colors.Length, Is.EqualTo(mesh.vertexCount));
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("SpaceEngine/Terrain"));
                visibleMesh = true;
            }
            Assert.That(visibleMesh, Is.True, "A live MeshRenderer must use generated terrain vertices, colors and normals.");
            // Pixel correctness is checked by the separate captured acceptance route.
        }

        [UnityTest]
        public IEnumerator GroundConstraintUsesTheSeededSurfaceAndConfiguredClearance()
        {
            Double3 direction = app.CameraFromPlanet.Normalized;
            double height = TerrainNoise.Height(direction, app.Planet);
            if (app.Planet.HasOcean) height = Math.Max(0, height);
            var underSurface = app.PlanetPosition(0).Add(direction * (app.Planet.Radius + height - 100));
            app.SetObserver(underSurface, app.ViewCamera.transform.rotation);
            app.ConstrainGround();
            double measured = app.Observer.RelativeTo(app.PlanetPosition(0)).Length - app.Planet.Radius - height;
            Assert.That(measured, Is.EqualTo(settings.MinimumClearance).Within(0.0003));
            Assert.That(app.Observer.Equals(underSurface), Is.False);
            yield return null;
            Assert.That(app.Altitude, Is.EqualTo(settings.MinimumClearance).Within(0.0003));
        }

        [UnityTest]
        public IEnumerator ApproachingAnotherPlanetSelectsItsLocalFrameWithoutTeleporting()
        {
            const int planetIndex = 1;
            PlanetDefinition planet = app.System.Planets[planetIndex];
            var position = app.PlanetPosition(planetIndex).Add(new Double3(0, planet.Radius + 5000, 0));
            app.SetObserver(position, Quaternion.identity);
            app.SelectNearestBody();
            Assert.That(app.SelectedPlanet, Is.EqualTo(planetIndex));
            Assert.That(app.Observer.Equals(position), Is.True, "Body selection changes the frame, not the SI observer position.");
            yield return null;
            Assert.That(app.Planet.Seed, Is.EqualTo(planet.Seed));
            Assert.That(app.Altitude, Is.InRange(5000 - planet.TerrainHeight, 5000 + planet.TerrainHeight));
        }

        [UnityTest]
        public IEnumerator StreamingNeighbourRetainsObserverAndReleasesPreviousPlanetObjects()
        {
            var neighbour = UniverseGenerator.GenerateSystem(settings.UniverseSeed, 1, 0, 0);
            var position = neighbour.Position.Add(new Double3(0, 0, PhysicalConstants.AstronomicalUnit * 2));
            var previousTerrains = (PlanetTerrain[])app.Terrains.Clone();
            app.SetObserver(position, app.ViewCamera.transform.rotation);
            app.StreamNearestSystem();
            Assert.That(app.CellX, Is.EqualTo(1)); Assert.That(app.CellY, Is.EqualTo(0)); Assert.That(app.CellZ, Is.EqualTo(0));
            Assert.That(app.Observer.Equals(position), Is.True, "Streaming must preserve sector and local values exactly.");
            Assert.That(app.System.Seed, Is.EqualTo(neighbour.Seed));
            yield return null;
            yield return null;
            foreach (var previous in previousTerrains) Assert.That(previous == null, Is.True, "Previous system terrain GameObjects must be released.");
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator CentimetreMotionAndSaveRestoreSurviveGalacticCoordinates()
        {
            app.SimulationTime = 123456.789;
            app.LoadSystem(312000, -214000, 102000);
            app.FramePlanet(0, 5000);
            var before = app.Observer;
            var position = before.Add(new Double3(.01, -.02, .03));
            var rotation = Quaternion.Euler(11, 27, 3);
            app.SetObserver(position, rotation);
            Double3 movement = app.Observer.RelativeTo(before);
            Assert.That(movement.x, Is.EqualTo(.01).Within(.0001));
            Assert.That(movement.y, Is.EqualTo(-.02).Within(.0001));
            Assert.That(movement.z, Is.EqualTo(.03).Within(.0001));
            int expectedSeed = app.System.Seed, expectedUniverseSeed = settings.UniverseSeed;
            app.SaveFlight();
            Assert.That(File.Exists(savePath), Is.True);
            Assert.That(new FileInfo(savePath).Length, Is.GreaterThan(100));
            app.Settings.UniverseSeed += 1;
            app.SimulationTime = 0;
            app.LoadSystem(0, 0, 0);
            app.SetObserver(position.Add(new Double3(500, 0, 0)), Quaternion.identity);
            app.LoadFlight();
            Assert.That(app.CellX, Is.EqualTo(312000)); Assert.That(app.CellY, Is.EqualTo(-214000)); Assert.That(app.CellZ, Is.EqualTo(102000));
            Assert.That(app.Settings.UniverseSeed, Is.EqualTo(expectedUniverseSeed));
            Assert.That(app.System.Seed, Is.EqualTo(expectedSeed));
            Assert.That(app.SimulationTime, Is.EqualTo(123456.789).Within(1e-8));
            Assert.That(app.Observer.RelativeTo(position).Length, Is.LessThan(.0001));
            Assert.That(Quaternion.Angle(app.ViewCamera.transform.rotation, rotation), Is.LessThan(.01f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidSaveDoesNotPartiallyReplaceTheCurrentFlight()
        {
            UniversalPosition before = app.Observer;
            int beforeSeed = app.System.Seed, beforeUniverseSeed = app.Settings.UniverseSeed;
            double beforeTime = app.SimulationTime;
            File.WriteAllText(savePath, "{\"Version\":1,\"Seed\":1234,\"Time\":123,\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0}}");
            LogAssert.Expect(LogType.Exception, new Regex("Invalid orientation"));
            app.LoadFlight();
            Assert.That(app.Observer.Equals(before), Is.True);
            Assert.That(app.System.Seed, Is.EqualTo(beforeSeed));
            Assert.That(app.Settings.UniverseSeed, Is.EqualTo(beforeUniverseSeed));
            Assert.That(app.SimulationTime, Is.EqualTo(beforeTime));
            Assert.That(app.Notice, Does.StartWith("Restore failed:"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator KeyboardEventsDriveRealUpdateFlightAndEngageContinuousDescent()
        {
            Keyboard previousKeyboard = Keyboard.current;
            InputSettings previousInputSettings = InputSystem.settings;
            Assert.That(previousInputSettings, Is.Not.Null);
            InputSettings.BackgroundBehavior previousBackground = previousInputSettings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousEditorRouting = previousInputSettings.editorInputBehaviorInPlayMode;
            Keyboard keyboard = null;
            try
            {
                // Synthetic events must reach the game in unattended Editor runs.
                // Keep the engine-owned settings instance: replacing HideAndDontSave
                // defaults causes InputManager to destroy the previous object.
                // Restore these scoped runtime options in finally instead.
                previousInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                previousInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                Assert.That(InputSystem.settings.updateMode, Is.EqualTo(InputSettings.UpdateMode.ProcessEventsInDynamicUpdate));
                keyboard = InputSystem.AddDevice<Keyboard>("Explorer integration keyboard");
                keyboard.MakeCurrent();
                app.TimeRate = 0;
                app.SpeedMultiplier = .01;
                app.VerificationOwnsFlight = false;
                UniversalPosition before = app.Observer;
                Double3 forward = Double3.FromVector3(app.ViewCamera.transform.forward);
                double initialTime = app.SimulationTime;

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                for (int i = 0; i < 4 && app.Observer.RelativeTo(before).Length < .001; i++) yield return null;
                Double3 movement = app.Observer.RelativeTo(before);
                Assert.That(movement.Length, Is.GreaterThan(.001), "A queued W state must move the observer through UniverseApp.Update.");
                Assert.That(Double3.Dot(movement, forward), Is.GreaterThan(0), "W must move forward in the camera orientation.");
                Assert.That(app.Speed, Is.GreaterThan(0));
                Assert.That(app.SimulationTime, Is.EqualTo(initialTime), "The movement must come from input while orbital time is paused.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                for (int i = 0; i < 4; i++)
                {
                    yield return null;
                    if (app.Speed == 0) break;
                }
                Assert.That(keyboard.wKey.isPressed, Is.False);
                Assert.That(app.Speed, Is.EqualTo(0), "Releasing W must stop manual translation.");
                double beforeDescent = app.Altitude;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.G));
                for (int i = 0; i < 4 && app.Navigation != "CONTINUOUS DESCENT"; i++) yield return null;
                Assert.That(app.Navigation, Is.EqualTo("CONTINUOUS DESCENT"), "G must reach the actual wasPressedThisFrame input binding.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                Assert.That(keyboard.gKey.isPressed, Is.False);
                Assert.That(app.Navigation, Is.EqualTo("CONTINUOUS DESCENT"));
                Assert.That(app.Altitude, Is.LessThan(beforeDescent), "The descent must continue after G is released.");
            }
            finally
            {
                if (app != null) { app.VerificationOwnsFlight = true; app.StopNavigation(); }
                if (keyboard != null && keyboard.added)
                {
                    InputSystem.ResetDevice(keyboard);
                    InputSystem.RemoveDevice(keyboard);
                }
                previousInputSettings.backgroundBehavior = previousBackground;
                previousInputSettings.editorInputBehaviorInPlayMode = previousEditorRouting;
                if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            }
            Assert.That(keyboard.added, Is.False, "The injected keyboard must not remain registered after the test.");
            Assert.That(InputSystem.settings, Is.SameAs(previousInputSettings));
            Assert.That(previousInputSettings.backgroundBehavior, Is.EqualTo(previousBackground));
            Assert.That(previousInputSettings.editorInputBehaviorInPlayMode, Is.EqualTo(previousEditorRouting));
        }
    }
}

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
using UnityEngine.Rendering;
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
        string legacySavePath;
        readonly Dictionary<string, byte[]> previousSaveFiles = new Dictionary<string, byte[]>();
        readonly List<UniverseApp> suspendedApps = new List<UniverseApp>();
        readonly List<Camera> suspendedCameras = new List<Camera>();

        [Serializable]
        sealed class SaveHeader
        {
            public int Version, GenerationVersion, ViewMode;
            public GalaxyDefinition Galaxy;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousScene = SceneManager.GetActiveScene();
            previousVsync = QualitySettings.vSyncCount; previousTargetFrameRate = Application.targetFrameRate;
            previousFog = RenderSettings.fog; previousAmbient = RenderSettings.ambientLight; previousSkybox = RenderSettings.skybox;
            savePath = Path.Combine(Application.persistentDataPath, "flight-v2.json");
            legacySavePath = Path.Combine(Application.persistentDataPath, "flight.json");
            foreach (string path in new[] { savePath, savePath + ".bak", savePath + ".tmp", legacySavePath, legacySavePath + ".bak", legacySavePath + ".tmp" })
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
            settings.GalaxyParticleCount = 1000;
            settings.GalaxyResolutionScale = .2f;
            settings.GalaxyVolumeSamples = 32;
            if (settings.Galaxy == null) settings.Galaxy = GalaxyModel.Default(settings.UniverseSeed);
            settings.Galaxy.Seed = settings.UniverseSeed;
            settings.TerrainResolution = 6;
            settings.TerrainMaterialResolution = 128;
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
            int occupiedCells = 0;
            for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
                if (UniverseGenerator.IsCellOccupied(settings.UniverseSeed, x, y, z, settings.Galaxy)) occupiedCells++;
            Assert.That(app.Stars.Count, Is.EqualTo(occupiedCells));
            Assert.That(app.Stars.Count, Is.InRange(2, 124), "The local catalogue must resolve occupied cells rather than filling every cell.");
            Assert.That(app.SavePath, Is.EqualTo(savePath));
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
        public IEnumerator SelectedSolidMaterialMapAllocatesAtConfiguredResolutionWithoutChangingPhysicalHeight()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supports2DArrayTextures)
                Assert.Ignore("This graphics backend cannot allocate the procedural surface array.");
            const int planetIndex = 1;
            PlanetDefinition body = app.System.Planets[planetIndex];
            Assert.That(body.IsLandable, Is.True);
            var directions = new[] { new Double3(.23,.81,-.54).Normalized, new Double3(-.76,.12,.64).Normalized,
                new Double3(.19,-.93,.31).Normalized };
            var physicalHeights = new double[directions.Length];
            for (int i = 0; i < directions.Length; i++) physicalHeights[i] = TerrainNoise.Height(directions[i], body);
            app.FramePlanet(planetIndex, body.Radius * 2);
            UniversalPosition position = app.Observer;
            PlanetTerrain terrain = app.Terrains[planetIndex];
            for (int frame = 0; frame < 30 && !terrain.MaterialMapReady; frame++) yield return null;
            Assert.That(terrain.MaterialMapReady, Is.True, "The selected orbital planet must actually bake its seeded surface image.");
            Assert.That(terrain.MaterialMapResolution, Is.EqualTo(settings.TerrainMaterialResolution));
            long expectedBytes = (long)settings.TerrainMaterialResolution * settings.TerrainMaterialResolution * 6 * 4 * 4 / 3;
            Assert.That(terrain.MaterialMapBytes, Is.EqualTo(expectedBytes), "RFloat six-face mip storage must use the configured resolution.");
            for (int i = 0; i < directions.Length; i++)
            {
                Assert.That(TerrainNoise.Height(directions[i], body), Is.EqualTo(physicalHeights[i]), "Material detail must not rewrite physical geology.");
                Assert.That(app.SurfaceHeight(directions[i]), Is.EqualTo(body.HasOcean ? Math.Max(0, physicalHeights[i]) : physicalHeights[i]));
            }
            Assert.That(app.Observer.Equals(position), Is.True);
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator OceanicWeatherRendersInOrbitAndFadesBeforeTheActualSurfaceWithoutMovingFlight()
        {
            Assert.That(app.System.Planets[0].Kind, Is.EqualTo(PlanetKind.Oceanic));
            app.FramePlanet(0, app.System.Planets[0].Radius);
            UniversalPosition orbit = app.Observer;
            yield return null;
            yield return null;
            var cloud = app.Clouds[0];
            Assert.That(cloud.HasCloudLayer, Is.True);
            Assert.That(cloud.Visible, Is.True, "The integrated optical weather renderer must be active in orbit.");
            Assert.That(cloud.ObserverFade, Is.EqualTo(1));
            Assert.That(app.Observer.Equals(orbit), Is.True);
            Assert.That(cloud.GetComponentsInChildren<Collider>().Length, Is.Zero);
            app.SetAltitude(2, true);
            UniversalPosition surface = app.Observer;
            double physicalHeight = app.SurfaceHeight(app.CameraFromPlanet.Normalized);
            yield return null;
            yield return null;
            Assert.That(cloud.Visible, Is.False, "The orbital thin layer must disappear before entry instead of becoming a ground ceiling.");
            Assert.That(cloud.ObserverFade, Is.Zero);
            Assert.That(app.Observer.Equals(surface), Is.True, "Cloud fade must not alter the SI observer or landing clearance.");
            app.ConstrainGround();
            Assert.That(app.Observer.RelativeTo(surface).Length, Is.LessThan(.0003));
            Assert.That(app.SurfaceHeight(app.CameraFromPlanet.Normalized), Is.EqualTo(physicalHeight).Within(.0003));
            Assert.That(app.Altitude, Is.EqualTo(settings.MinimumClearance).Within(.0003));
            Assert.That(app.Terrains[0].gameObject.activeSelf, Is.True);
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
        }

        [UnityTest]
        public IEnumerator GalacticVolumeCachesStableFlightAndInvalidatesOnAstronomicalMotionAndViewRotation()
        {
            app.FrameGalaxy();
            yield return null;
            yield return null;
            UniversalPosition fixedObserver = app.Observer;
            Quaternion fixedRotation = app.ViewCamera.transform.rotation;
            int firstRevision = app.GalacticLight.RenderRevision;
            Assert.That(firstRevision, Is.GreaterThan(0));
            Assert.That(app.GalacticLight.RadianceTexture, Is.Not.Null);
            for (int frame = 0; frame < 3; frame++) yield return null;
            Assert.That(app.GalacticLight.RenderRevision, Is.EqualTo(firstRevision), "A stationary observer/view must reuse the radiance cache.");
            Assert.That(app.Observer.Equals(fixedObserver), Is.True);
            UniversalPosition moved = fixedObserver.Add(new Double3(50 * PhysicalConstants.LightYear, 0, 0));
            app.SetObserver(moved, fixedRotation);
            yield return null;
            yield return null;
            int movedRevision = app.GalacticLight.RenderRevision;
            Assert.That(movedRevision, Is.GreaterThan(firstRevision), "Moving fifty real light years must re-integrate the finite galactic volume.");
            Assert.That(app.Observer.Equals(moved), Is.True);
            for (int frame = 0; frame < 2; frame++) yield return null;
            Assert.That(app.GalacticLight.RenderRevision, Is.EqualTo(movedRevision));
            app.SetObserver(moved, fixedRotation * Quaternion.Euler(0, 5, 0));
            yield return null;
            Assert.That(app.GalacticLight.RenderRevision, Is.GreaterThan(movedRevision), "Rotating the real flight camera must invalidate directional radiance.");
            Assert.That(app.Observer.Equals(moved), Is.True);
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero), "Float camera origin stays local while the astronomical observer moves.");
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
            var entry = app.NearestCatalogueSystem();
            Assert.That(entry.System, Is.Not.Null, "The starting volume must contain an occupied neighbour.");
            var neighbour = entry.System;
            Assert.That(UniverseGenerator.IsCellOccupied(settings.UniverseSeed, entry.X, entry.Y, entry.Z, app.Settings.Galaxy), Is.True);
            var position = neighbour.Position.Add(new Double3(0, 0, PhysicalConstants.AstronomicalUnit * 2));
            var previousTerrains = (PlanetTerrain[])app.Terrains.Clone();
            app.SetObserver(position, app.ViewCamera.transform.rotation);
            app.StreamNearestSystem();
            Assert.That(app.CellX, Is.EqualTo(entry.X)); Assert.That(app.CellY, Is.EqualTo(entry.Y)); Assert.That(app.CellZ, Is.EqualTo(entry.Z));
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
            File.WriteAllText(savePath, "{\"Version\":2,\"GenerationVersion\":2,\"Seed\":1234,\"Time\":123,\"Rotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":0}}");
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
        public IEnumerator GasGiantApproachStopsAboveCloudsWithoutClaimingALanding()
        {
            const int gasIndex = 3;
            Assert.That(app.System.Planets[gasIndex].Kind, Is.EqualTo(PlanetKind.GasGiant));
            app.FramePlanet(gasIndex, 5000);
            var direction = app.CameraFromPlanet.Normalized;
            app.SetObserver(app.PlanetPosition(gasIndex).Add(direction * (app.Planet.Radius + app.Settings.GasCloudSafetyClearance + 5000)),
                app.ViewCamera.transform.rotation);
            app.BeginDescent();
            Assert.That(app.Navigation, Does.Contain("NO SOLID SURFACE"));
            for (int i = 0; i < 100 && !app.Navigation.StartsWith("HOLDING ABOVE CLOUDS"); i++) app.AdvanceRadialFlight(.25);
            yield return null;
            Assert.That(app.Planet.IsLandable, Is.False);
            Assert.That(app.Navigation, Does.StartWith("HOLDING ABOVE CLOUDS"));
            Assert.That(app.Navigation, Does.Not.Contain("LANDED"));
            Assert.That(app.Altitude, Is.EqualTo(app.Settings.GasCloudSafetyClearance).Within(.001));
            Assert.That(app.Terrains[gasIndex].gameObject.activeSelf, Is.False);
            Assert.That(app.Terrains[gasIndex].ActivePatchCount, Is.Zero);
            Assert.That(app.Celestials[gasIndex].IsGasBody, Is.True);
            Assert.That(app.Celestials[gasIndex].HasRings, Is.True);
            app.SetObserver(app.PlanetPosition(gasIndex).Add(direction * (app.Planet.Radius - 100)), app.ViewCamera.transform.rotation);
            app.ConstrainGround();
            double clearance = app.Observer.RelativeTo(app.PlanetPosition(gasIndex)).Length - app.Planet.Radius;
            Assert.That(clearance, Is.EqualTo(app.Settings.GasCloudSafetyClearance).Within(.001));
        }

        [UnityTest]
        public IEnumerator MoonWorldPositionIncludesItsMovingParentAndParentMassOrbit()
        {
            const int moonIndex = 5;
            var moon = app.System.Planets[moonIndex];
            Assert.That(moon.IsMoon, Is.True);
            var parent = app.System.Planets[moon.ParentIndex];
            double moonPeriod = Orbit.Period(moon, parent.Mass);
            foreach (double time in new[] { 0, moonPeriod * .25, moonPeriod * .75 })
            {
                app.SimulationTime = time;
                Double3 expected = Orbit.Evaluate(moon, parent.Mass, time);
                Double3 relativeOffset = app.BodyOffset(moonIndex) - app.BodyOffset(moon.ParentIndex);
                Double3 relativePosition = app.PlanetPosition(moonIndex).RelativeTo(app.PlanetPosition(moon.ParentIndex));
                Assert.That((relativeOffset - expected).Length, Is.LessThan(.003));
                Assert.That((relativePosition - expected).Length, Is.LessThan(.003));
            }
            app.FramePlanet(moonIndex, 5000);
            Assert.That(app.SelectedPlanet, Is.EqualTo(moonIndex));
            Assert.That(app.Planet.Seed, Is.EqualTo(moon.Seed));
            yield return null;
            Assert.That(app.CameraFromPlanet.Length, Is.LessThan(moon.Radius + moon.TerrainHeight + 6000));
        }

        [UnityTest]
        public IEnumerator GalaxyOverviewAndStreamingRetainTheActualAstronomicalObserver()
        {
            app.FrameGalaxy();
            Assert.That(app.GalaxyOverview, Is.True);
            UniversalPosition overview = app.Observer;
            Double3 galactic = GalaxyModel.ToGalacticLy(overview, app.Settings.Galaxy);
            Assert.That(galactic.y, Is.GreaterThan(100000));
            Assert.That(galactic.z, Is.GreaterThan(120000));
            Assert.That(app.Observer.RelativeTo(UniversalPosition.Origin).Length, Is.GreaterThan(100000 * PhysicalConstants.LightYear));
            app.StreamNearestSystem();
            Assert.That(app.Observer.Equals(overview), Is.True);
            yield return null;
            Assert.That(app.Observer.Equals(overview), Is.True);
            Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(app.Stars.Count, Is.Zero, "An exterior viewpoint must not fill empty intergalactic cells with local stars.");
            var renderer = app.Galaxy.GetComponent<MeshRenderer>();
            Vector4 uniform = renderer.sharedMaterial.GetVector("_ObserverLy");
            Assert.That((new Double3(uniform.x, uniform.y, uniform.z) - galactic).Length, Is.LessThan(.1));
            app.ReturnHome();
            Assert.That(app.GalaxyOverview, Is.False);
            Assert.That(app.CellX, Is.Zero); Assert.That(app.CellY, Is.Zero); Assert.That(app.CellZ, Is.Zero);
            Assert.That(app.Observer.RelativeTo(UniversalPosition.Origin).Length, Is.LessThan(PhysicalConstants.LightYear * .01));
        }

        [UnityTest]
        public IEnumerator GalaxyGpuVerticesContainTheSeededThreeDimensionalPopulation()
        {
            if (!SystemInfo.supportsAsyncGPUReadback) Assert.Ignore("This graphics backend cannot read back the uploaded galaxy vertex buffer.");
            Assert.That(app.Galaxy.ParticleCount, Is.EqualTo(1000));
            Mesh mesh = app.Galaxy.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(mesh.vertexCount, Is.EqualTo(app.Galaxy.ParticleCount * 4));
            var expected = GalaxyModel.GenerateRepresentatives(app.Settings.Galaxy, app.Galaxy.ParticleCount);
            var buffer = mesh.GetVertexBuffer(0);
            try
            {
                var request = AsyncGPUReadback.Request(buffer);
                for (int frame = 0; frame < 120 && !request.done; frame++) yield return null;
                Assert.That(request.done, Is.True);
                Assert.That(request.hasError, Is.False);
                var values = request.GetData<float>();
                int stride = mesh.GetVertexBufferStride(0) / sizeof(float);
                int offset = mesh.GetVertexAttributeOffset(VertexAttribute.Position) / sizeof(float);
                float minimumY = float.MaxValue, maximumY = float.MinValue;
                for (int i = 0; i < app.Galaxy.ParticleCount; i++)
                {
                    int index = i * 4 * stride + offset;
                    var position = new Vector3(values[index], values[index + 1], values[index + 2]);
                    Assert.That(position, Is.EqualTo(expected[i].PositionLy.ToVector3()), "GPU source vertices must retain each seeded spatial position.");
                    minimumY = Math.Min(minimumY, position.y); maximumY = Math.Max(maximumY, position.y);
                }
                Assert.That(maximumY - minimumY, Is.GreaterThan(1000), "The uploaded population must have actual galactic height, not a flat image plane.");
            }
            finally { buffer.Dispose(); }
        }

        [UnityTest]
        public IEnumerator VersionTwoSavePreservesLegacyBytesAndRestoresGalaxyConfiguration()
        {
            byte[] legacy = System.Text.Encoding.UTF8.GetBytes("{\"Version\":1,\"Seed\":98765,\"retained\":\"legacy flight\"}");
            File.WriteAllBytes(legacySavePath, legacy);
            if (File.Exists(savePath)) File.Delete(savePath);
            UniversalPosition before = app.Observer;
            app.LoadFlight();
            Assert.That(app.Notice, Does.Contain("v1 save retained"));
            Assert.That(app.Observer.Equals(before), Is.True);
            app.Settings.Galaxy.ArmPitchRadians = .29;
            app.Settings.Galaxy.DiskScaleLengthLy = 9700;
            app.SaveFlight();
            Assert.That(app.SavePath, Is.EqualTo(savePath));
            CollectionAssert.AreEqual(legacy, File.ReadAllBytes(legacySavePath));
            var header = JsonUtility.FromJson<SaveHeader>(File.ReadAllText(savePath));
            Assert.That(header.Version, Is.EqualTo(2));
            Assert.That(header.GenerationVersion, Is.EqualTo(UniverseGenerator.GenerationVersion));
            Assert.That(header.Galaxy, Is.Not.Null);
            Assert.That(header.Galaxy.ArmPitchRadians, Is.EqualTo(.29));
            app.Settings.Galaxy.ArmPitchRadians = .18;
            app.Settings.Galaxy.DiskScaleLengthLy = 8100;
            app.LoadFlight();
            Assert.That(app.Notice, Is.EqualTo("Flight restored"));
            Assert.That(app.Settings.Galaxy.ArmPitchRadians, Is.EqualTo(.29));
            Assert.That(app.Settings.Galaxy.DiskScaleLengthLy, Is.EqualTo(9700));
            Assert.That(app.Galaxy.Definition.ArmPitchRadians, Is.EqualTo(.29), "The rendered galaxy must adopt the restored configuration even when its seed did not change.");
            Assert.That(app.Galaxy.Definition.DiskScaleLengthLy, Is.EqualTo(9700));
            CollectionAssert.AreEqual(legacy, File.ReadAllBytes(legacySavePath));
            yield return null;
        }

        StarCatalogueRenderer.Entry StellarFixture(StellarKind kind)
        {
            // Known generation-v2 fixtures keep the ordinary path constant-time. The seeded search
            // also supports a different shipped universe seed; admission is separate from this scale check.
            long knownX = kind == StellarKind.NeutronStar ? -128 : -96;
            long knownZ = kind == StellarKind.NeutronStar ? 118 : 11;
            var known = UniverseGenerator.GenerateSystem(app.Settings.UniverseSeed, knownX, 0, knownZ, app.Settings.Galaxy);
            if (known.Kind == kind) return new StarCatalogueRenderer.Entry { X = knownX, Y = 0, Z = knownZ, System = known };
            for (int i = 1; i <= 4096; i++)
            {
                long x = i, y = i % 19, z = -i % 29;
                var candidate = UniverseGenerator.GenerateSystem(app.Settings.UniverseSeed, x, y, z, app.Settings.Galaxy);
                if (candidate.Kind == kind) return new StarCatalogueRenderer.Entry { X = x, Y = y, Z = z, System = candidate };
            }
            Assert.Fail("No deterministic " + kind + " fixture was found within the bounded seed scan.");
            return default;
        }

        [UnityTest]
        public IEnumerator CompactStellarReferenceHasVisibleScaleAndSurvivesSaveRestore()
        {
            foreach (StellarKind kind in new[] { StellarKind.NeutronStar, StellarKind.BlackHole })
            {
                var fixture = StellarFixture(kind);
                app.LoadSystem(fixture.X, fixture.Y, fixture.Z);
                app.FrameStar();
                Assert.That(app.System.Kind, Is.EqualTo(kind));
                Assert.That(app.StellarView, Is.True);
                Assert.That(app.GalaxyOverview || app.NucleusView, Is.False);
                double clearance = app.Observer.RelativeTo(app.System.Position).Length - app.System.StarRadius;
                Assert.That(app.Altitude, Is.EqualTo(clearance).Within(.001), "Stellar telemetry must use star clearance rather than a distant selected planet.");
                Assert.That(clearance * app.RenderScale, Is.GreaterThanOrEqualTo(1000));
                Assert.That(app.System.StarRadius * app.RenderScale, Is.GreaterThan(1000), "Compact objects must remain visible outside the camera near plane.");
                UniversalPosition savedObserver = app.Observer;
                double savedScale = app.RenderScale;
                app.SaveFlight();
                Assert.That(JsonUtility.FromJson<SaveHeader>(File.ReadAllText(savePath)).ViewMode, Is.EqualTo(3));
                app.FramePlanet(0, settings.InitialAltitude);
                Assert.That(app.StellarView, Is.False);
                app.LoadFlight();
                Assert.That(app.Notice, Is.EqualTo("Flight restored"));
                Assert.That(app.StellarView, Is.True);
                Assert.That(app.GalaxyOverview || app.NucleusView, Is.False);
                Assert.That(app.Observer.Equals(savedObserver), Is.True, "Restoring a view must preserve the astronomical observer exactly.");
                Assert.That(app.RenderScale, Is.EqualTo(savedScale));
                yield return null;
                Assert.That(app.ViewCamera.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(app.RenderScale, Is.EqualTo(savedScale));
            }
        }

        [UnityTest]
        public IEnumerator BodyGalaxyAndNucleusReferenceModesRoundTripWithTheirObserverAndScale()
        {
            for (int mode = 0; mode <= 2; mode++)
            {
                if (mode == 0) app.FramePlanet(1, settings.InitialAltitude);
                else if (mode == 1) app.FrameGalaxy();
                else app.FrameNucleus();
                UniversalPosition observer = app.Observer;
                double scale = app.RenderScale;
                int selected = app.SelectedPlanet;
                app.SaveFlight();
                Assert.That(JsonUtility.FromJson<SaveHeader>(File.ReadAllText(savePath)).ViewMode, Is.EqualTo(mode));
                app.FramePlanet(0, settings.InitialAltitude);
                app.LoadFlight();
                Assert.That(app.Notice, Is.EqualTo("Flight restored"));
                Assert.That(app.GalaxyOverview, Is.EqualTo(mode == 1));
                Assert.That(app.NucleusView, Is.EqualTo(mode == 2));
                Assert.That(app.StellarView, Is.False);
                Assert.That(app.SelectedPlanet, Is.EqualTo(selected));
                Assert.That(app.Observer.Equals(observer), Is.True);
                Assert.That(app.RenderScale, Is.EqualTo(scale));
                yield return null;
                Assert.That(app.Observer.Equals(observer), Is.True);
                Assert.That(app.RenderScale, Is.EqualTo(scale));
            }
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

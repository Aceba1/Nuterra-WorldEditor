using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using Harmony;
namespace Maritaria.WorldBuilder
{
    public static class Initiate
    {
        public static void INIT()
        {
            HarmonyInstance.Create("nuterra.worldeditor").PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
            new GameObject().AddComponent<EditorHotkey>();
        }
    }
	internal sealed class EditorHotkey : MonoBehaviour
	{
        float GeneralRot = 0f;
		public bool EditorEnabled { get; set; }
		private TerrainObject SelectedPrefab => _prefabs[_selectedIndex];

		private List<TerrainObject> _prefabs;
		private int _selectedIndex;
		private GameObject _ghost;
        public static bool Ready = false;

		private void Start()
		{
            Singleton.DoOnceAfterStart(WaitAfterSingleton);
		}

        private void WaitAfterSingleton()
        {
            Invoke("Prepare", 20f);
        }

        private void Prepare()
        {
            try
            {
                TerrainObjectTable table = (typeof(ManSpawn).GetField("m_TerrainObjectTable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(ManSpawn.inst)) as TerrainObjectTable;
                Dictionary<string, TerrainObject> m_GUIDToPrefabLookup = (typeof(TerrainObjectTable).GetField("m_GUIDToPrefabLookup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(table)) as Dictionary<string, TerrainObject>;
                _prefabs = new List<TerrainObject>();
                foreach (var value in m_GUIDToPrefabLookup)
                {
                    if (value.Value != null && value.Value is TerrainObject)
                        _prefabs.Add(value.Value);
                }
                Ready = true;
            }
            catch (Exception E)
            {
                Console.WriteLine("EXCEPTION: (NuterraWorldEditor) " + E.Message + "\n" + E.StackTrace);
            }
        }

		private void Update()
		{
            if (!Ready) return;
			bool stateChanged = Input.GetKey(KeyCode.LeftAlt) && Input.GetKeyDown(KeyCode.E);
			if (stateChanged)
			{
				EditorEnabled = !EditorEnabled;
			}
			bool shouldUpdate = EditorEnabled;
			if (ManPauseGame.inst.IsPaused || EventSystem.current.IsPointerOverGameObject() || !ManGameMode.inst.IsCurrent<ModeMisc>())
			{
				stateChanged |= EditorEnabled;
				shouldUpdate = false;
			}
			if (stateChanged)
			{
				if (EditorEnabled)
				{
                    if (!_ghost)
					{
						_ghost = CreateGhost(Vector3.zero);
					}
				}
				else
                {
                    Unghost();
                }
			}
			if (shouldUpdate)
			{
				Update_Editor();
			}
		}

		private void OnGUI()
		{
			if (EditorEnabled)
			{
                GUI.Label(new Rect(Screen.width * .5f - 100, Screen.height * .8f - 50, 200, 100), $"Selection: {SelectedPrefab.name}");
			}
		}

		private void Update_Editor()
		{
			var mousePos = Input.mousePosition;
			RaycastHit ray;
            if (_ghost)
            {
                _ghost.transform.position = Vector3.down * 500f;
            }
			bool hit = Physics.Raycast(Singleton.camera.ScreenPointToRay(mousePos), out ray, float.MaxValue, Globals.inst.layerTerrain.mask | Globals.inst.layerScenery.mask, QueryTriggerInteraction.Ignore);

            if (Input.GetKey(KeyCode.LeftAlt))
            {
                ray.normal = Vector3.up;
                ray.point = new Vector3(ray.point.x, ray.point.y, ray.point.z);
            }
			if (Input.GetMouseButtonDown(0 /*LMB*/))
			{
				PlaceRock(hit, ray);
			}
			if (Input.GetKeyDown(KeyCode.Backspace))
			{
				DeleteRock(hit, ray);
			}

			UpdatePrefabSelection(ray, hit);
            if (Input.GetKey(KeyCode.RightArrow))
            {
                GeneralRot += 1f;
            }
            if (Input.GetKey(KeyCode.LeftArrow))
            {
                GeneralRot -= 1f;
            }
            Mathf.Repeat(GeneralRot, 360);
            UpdateGhost(ray);
		}

		private void UpdatePrefabSelection(RaycastHit ray, bool hit)
		{
			if (Input.GetKeyDown(KeyCode.DownArrow))
			{
				SelectPreviousPrefab(ray);
			}
			if (Input.GetKeyDown(KeyCode.UpArrow))
			{
				SelectNextPrefab(ray);
			}
		}

        /// <summary>
        /// False for left, True for right
        /// </summary>
        bool MoveDir = false;

		private void SelectPreviousPrefab(RaycastHit ray)
		{
			_selectedIndex--;
            MoveDir = false;
			if (_selectedIndex < 0)
			{
				_selectedIndex = _prefabs.Count - 1;
			}
			UpdateGhostModel(ray);
		}

		private void SelectNextPrefab(RaycastHit ray)
		{
			_selectedIndex++;
            MoveDir = true;
			if (_selectedIndex >= _prefabs.Count - 1)
			{
				_selectedIndex = 0;
			}
			UpdateGhostModel(ray);
		}

        void RemoveObj(Transform obj)
        {
            Transform transform = obj.GetTopParent();
            var visible = Visible.FindVisibleUpwards(obj);
            if (visible)
            {
                visible.tileCache.tile.RemoveVisible(visible);
                visible.RemoveFromGame();
            }
            else
            {
                transform.Recycle();
            }
        }

		private void UpdateGhostModel(RaycastHit ray)
        {
            Unghost();
            _ghost = CreateGhost(ray.point);
		}

		private void UpdateGhost(RaycastHit ray)
		{
			if (_ghost)
			{
				_ghost.transform.position = ray.point;
				_ghost.transform.rotation = Quaternion.FromToRotation(Vector3.up, ray.normal) * Quaternion.Euler(0, GeneralRot, 0);
			}
		}

        System.Reflection.FieldInfo m_AnimatedTransform = typeof(ResourceDispenser).GetField("m_AnimatedTransform", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private void Unghost()
        {
            Transform[] array = _ghost.GetComponentsInChildren<Transform>();
            for (int i = 0; i < array.Length; i++)
            {
                try
                {
                    array[i].gameObject.layer = LayersInGhost[i];
                }
                catch (Exception E)
                {
                    Console.WriteLine(E.ToString());
                }
            }
            _ghost.transform.Recycle();
        }
        List<int> LayersInGhost = new List<int>();
		private GameObject CreateGhost(Vector3 pos)
		{
            GameObject obj = null;
            Retry:
            try
            {
                obj = PlaceRock(pos, Quaternion.identity).TerrainObject.gameObject;
                obj.layer = Globals.inst.layerTrigger;
                foreach (var collider in obj.GetComponentsInChildren<MeshCollider>())
                {
                    collider.enabled = false;
                }
                LayersInGhost.Clear();
                foreach (var transform in obj.GetComponentsInChildren<Transform>())
                {
                    LayersInGhost.Add(transform.gameObject.layer);
                    transform.gameObject.layer = Globals.inst.layerTrigger;
                }
                return obj;
            }
            catch(Exception E)
            {
                Console.WriteLine("EXCEPTION (WorldBuilder) " + E.Message + "\n" + (obj == null ? "Terrain object is null!" : ""));

                _prefabs.RemoveAt(_selectedIndex);

                if (_prefabs.Count == 0)
                {
                    throw new Exception("Nuterra-WorldEditor: There are no more prefabs!");
                }

                if (MoveDir)
                {
                    if (_selectedIndex >= _prefabs.Count - 1)
                    {
                        _selectedIndex = 0;
                    }
                    goto Retry;
                }

                _selectedIndex--;
                if (_selectedIndex < 0)
                {
                    _selectedIndex = _prefabs.Count - 1;
                }
                goto Retry;
            }
		}

		private void PlaceRock(bool hit, RaycastHit ray)
		{
			if (hit && (ray.transform.gameObject.layer == Globals.inst.layerTerrain || ray.transform.gameObject.layer == Globals.inst.layerScenery))
			{
				PlaceRock(ray.point, Quaternion.FromToRotation(Vector3.up, ray.normal) * Quaternion.Euler(0, GeneralRot, 0));
			}
		}
        
        private TerrainObject.SpawnedTerrainObjectData PlaceRock(Vector3 point, Quaternion direction)
		{
			return SelectedPrefab.SpawnFromPrefabAndAddToSaveData(point, direction);
		}

		private void DeleteRock(bool hit, RaycastHit ray)
		{

            if (hit)
            {
                var obj = ray.transform;
                try
                {
                    RemoveObj(obj);
                }
                catch { }
			}
		}
	}
}
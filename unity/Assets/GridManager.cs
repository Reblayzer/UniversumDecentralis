using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[System.Serializable]
public class CuboidType
{
    public string name;
    private string plotID;
    public GameObject prefab;
    public GameObject shockwavePrefab;
    public int length = 1;
    public int width = 1;
    public int height = 1;

    public void SetPlotID(string id)
    {
        plotID = id;
    }
}

public enum PlotType { Normal, Abandoned, Void }
public enum Ownership { Unclaimed, Yours, Opponent }

public class GridManager : MonoBehaviour
{
    [HideInInspector] public Ownership ownership = Ownership.Unclaimed;
    [HideInInspector] public int plotRow;
    [HideInInspector] public int plotCol;

    [Header("Grid Settings")]
    public int gridSize = 7;
    public GameObject tilePrefab;
    public CuboidType[] cuboidTypes;

    [Header("Materials")]
    public Material ghostMaterialValid;
    public Material ghostMaterialInvalid;

    [Header("UI References")]
    public GameObject buildingButtonsPanel;
    [SerializeField] private GameObject selectedCuboidUIPanel;
    [SerializeField] private TMP_Text selectedCuboidInfoText;
    [SerializeField] private Button upgradeButton;

    public GameObject SelectedCuboidUIPanel => selectedCuboidUIPanel;
    public TMP_Text SelectedCuboidInfoText => selectedCuboidInfoText;
    public Button UpgradeButton => upgradeButton;

    [Header("Audio")]
    public AudioClip placementSound;

    [Header("Marker Canvas Prefabs")]
    public GameObject claimedMarkerCanvasPrefab;
    public GameObject unclaimedMarkerCanvasPrefab;
    public GameObject abandonedMarkerCanvasPrefab;
    public GameObject opponentMarkerCanvasPrefab;

    public event Action OnCuboidPlaced;

    private AudioSource audioSource;
    private bool[,] occupiedTiles;
    private Tile[,] tileGrid;
    private bool initialized = false;
    public bool IsInitialized => initialized;

    private bool isRotated = false;
    private GameObject ghostObject;
    private string lastGhostName = "";
    private bool hasSelectedCuboid = false;
    private BuildingButtonSelector buttonSelector;
    private bool isEditMode = false;
    public int selectedIndex = 0;
    private Color currentHighlightColor;

    public bool CanPlace =>
        buildingButtonsPanel != null &&
        buildingButtonsPanel.activeSelf &&
        hasSelectedCuboid &&
        plotType == PlotType.Normal;

    public PlotType plotType = PlotType.Normal;
    public bool IsActive { get; private set; }

    void Start()
    {
        var triggerZone = new GameObject("PlotTrigger");
        triggerZone.transform.SetParent(transform);
        triggerZone.transform.localPosition = Vector3.zero;
        triggerZone.transform.localRotation = Quaternion.identity;
        triggerZone.transform.localScale = Vector3.one;

        var box = triggerZone.AddComponent<BoxCollider>();
        box.size = new Vector3(gridSize, 0.1f, gridSize);
        box.center = new Vector3(0, 0.01f, 0);
        box.isTrigger = true;
        triggerZone.layer = LayerMask.NameToLayer("Plot");

        var ptc = triggerZone.AddComponent<PlotTriggerController>();
        ptc.ownership = ownership;

        GameObject prefabToUse = null;
        if (plotType == PlotType.Abandoned)
        {
            prefabToUse = abandonedMarkerCanvasPrefab;
        }
        else if (plotType == PlotType.Normal)
        {
            switch (ownership)
            {
                case Ownership.Yours: prefabToUse = claimedMarkerCanvasPrefab; break;
                case Ownership.Opponent: prefabToUse = opponentMarkerCanvasPrefab; break;
                default: prefabToUse = unclaimedMarkerCanvasPrefab; break;
            }
        }

        if (prefabToUse != null)
        {
            var canv = Instantiate(prefabToUse, triggerZone.transform);
            canv.SetActive(false);
            canv.transform.localPosition = new Vector3(0, 2f, 0);
            canv.transform.localRotation = Quaternion.identity;
            canv.AddComponent<BillboardCanvas>();
            ptc.markerCanvas = canv;
        }

        float offset = gridSize / 2f - 0.5f;
        occupiedTiles = new bool[gridSize, gridSize];
        tileGrid = new Tile[gridSize, gridSize];

        for (int x = 0; x < gridSize; x++)
            for (int z = 0; z < gridSize; z++)
            {
                Vector3 pos = transform.position + new Vector3(x - offset, 0, z - offset);
                var tileObj = Instantiate(tilePrefab, pos, Quaternion.identity, transform);
                tileObj.layer = LayerMask.NameToLayer("Tile");

                var tileScript = tileObj.AddComponent<Tile>();
                tileScript.Init(new Vector2Int(x, z), this);
                tileGrid[x, z] = tileScript;
            }

        audioSource = GetComponent<AudioSource>();
        if (selectedCuboidUIPanel != null) selectedCuboidUIPanel.SetActive(false);
        if (buildingButtonsPanel != null) buildingButtonsPanel.SetActive(true);

        initialized = true;
    }

    public void SetActive(bool state)
    {
        IsActive = state;

        if (!state)
        {
            ClearHighlights();
            hasSelectedCuboid = false;
            if (selectedCuboidUIPanel != null) selectedCuboidUIPanel.SetActive(false);
            if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);
        }
        else
        {
            if (buildingButtonsPanel != null) buildingButtonsPanel.SetActive(true);
            if (buttonSelector != null && buttonSelector.CurrentIndex >= 0)
                SetSelectedCuboid(buttonSelector.CurrentIndex);
        }
    }

    private void HandleRotation()
    {
        if (Input.GetKeyDown(KeyCode.R) && CanPlace)
        {
            isRotated = !isRotated;
        }
    }

    public void TryPlaceCuboidAt(int startX, int startZ)
    {
        if (!CanPlace) return;
        if (cuboidTypes == null || cuboidTypes.Length == 0) return;
        if (selectedIndex < 0 || selectedIndex >= cuboidTypes.Length) return;

        CuboidType current = cuboidTypes[selectedIndex];
        int length = isRotated ? current.width : current.length;
        int width = isRotated ? current.length : current.width;

        for (int x = startX; x < startX + length; x++)
        {
            for (int z = startZ; z < startZ + width; z++)
            {
                if (x < 0 || z < 0 || x >= gridSize || z >= gridSize)
                    return;

                if (occupiedTiles[x, z])
                    return;
            }
        }

        float offset = gridSize / 2f - 0.5f;
        Vector3 spawnPos = transform.position + new Vector3(
            startX + length / 2f - 0.5f - offset,
            current.height / 2f,
            startZ + width / 2f - 0.5f - offset
        );

        Quaternion rotation = isRotated ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
        GameObject placed = Instantiate(current.prefab, spawnPos, rotation);

        SetLayerRecursive(placed, LayerMask.NameToLayer("Placed"));

        SelectableCuboid selectable = placed.AddComponent<SelectableCuboid>();
        selectable.cuboidName = current.name;
        selectable.infoPanel = SelectedCuboidUIPanel;
        selectable.infoDisplay = SelectedCuboidInfoText;
        selectable.upgradeButton = UpgradeButton;
        selectable.Init(this);

        for (int x = startX; x < startX + length; x++)
        {
            for (int z = startZ; z < startZ + width; z++)
            {
                occupiedTiles[x, z] = true;
            }
        }

        ClearHighlights();

        if (placementSound != null && audioSource != null)
        {
            audioSource.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
            audioSource.PlayOneShot(placementSound);
        }

        if (current.shockwavePrefab != null)
        {
            Vector3 effectPos = spawnPos - new Vector3(0, current.height / 2f - 0.2f, 0);
            GameObject vfx = Instantiate(current.shockwavePrefab, effectPos, Quaternion.identity);
            Destroy(vfx, 2f);
        }
        OnCuboidPlaced?.Invoke();
    }

    private void SetLayerRecursive(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer);
        }
    }

    public void SetSelectedCuboid(int index)
    {
        if (index >= 0 && index < cuboidTypes.Length)
        {
            selectedIndex = index;
            hasSelectedCuboid = true;
            ClearHighlights();

            // Force immediate ghost preview
            if (IsActive && isEditMode)
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                int mask = ~LayerMask.GetMask("Ghost", "Ignore Raycast");

                if (Physics.Raycast(ray, out hit, 100f, mask))
                {
                    Tile tile = hit.collider.GetComponent<Tile>();
                    if (tile != null && tile.GridManager == this)
                    {
                        bool isValid = HighlightTiles(tile.gridPosition.x, tile.gridPosition.y);
                        ShowGhost(tile.gridPosition.x, tile.gridPosition.y, cuboidTypes[selectedIndex], isRotated, isValid);
                    }
                }
            }
        }
    }

    public void ClearCuboidSelection()
    {
        hasSelectedCuboid = false;
        ClearHighlights();
    }

    public bool HighlightTiles(int startX, int startZ)
    {
        ClearHighlights();

        CuboidType current = cuboidTypes[selectedIndex];
        int length = isRotated ? current.width : current.length;
        int width = isRotated ? current.length : current.width;

        bool validPlacement = true;

        for (int x = startX; x < startX + length; x++)
        {
            for (int z = startZ; z < startZ + width; z++)
            {
                if (x >= gridSize || z >= gridSize || occupiedTiles[x, z])
                {
                    validPlacement = false;
                    break;
                }
            }
        }

        // ✅ Use the selector's ghost colors
        Color ghostColor = validPlacement
            ? buttonSelector.ghostCanPlaceColor
            : buttonSelector.ghostCanNotPlaceColor;

        for (int x = startX; x < startX + length; x++)
        {
            for (int z = startZ; z < startZ + width; z++)
            {
                if (x < gridSize && z < gridSize)
                    tileGrid[x, z]?.SetTemporaryHighlight(ghostColor);
            }
        }

        return validPlacement;
    }

    public void ClearHighlights()
    {
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                tileGrid[x, z]?.ClearHighlight();
            }
        }

        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostObject = null;
            lastGhostName = "";
        }
    }

    private void ShowGhost(int startX, int startZ, CuboidType current, bool rotated, bool isValid)
    {
        int length = rotated ? current.width : current.length;
        int width = rotated ? current.length : current.width;
        float offset = gridSize / 2f - 0.5f;

        Vector3 ghostPos = transform.position + new Vector3(
            startX + length / 2f - 0.5f - offset,
            current.height / 2f,
            startZ + width / 2f - 0.5f - offset
        );

        Quaternion rotation = rotated ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
        string ghostName = "Ghost_" + current.name;

        if (ghostObject == null || lastGhostName != ghostName)
        {
            if (ghostObject != null)
                Destroy(ghostObject);

            ghostObject = Instantiate(current.prefab, ghostPos, rotation);
            ghostObject.name = ghostName;
            lastGhostName = ghostName;

            SetLayerRecursive(ghostObject, LayerMask.NameToLayer("Ghost"));
            ApplyGhostMaterial(ghostObject, isValid);
        }
        else
        {
            ghostObject.transform.position = ghostPos;
            ghostObject.transform.rotation = rotation;
            ApplyGhostMaterial(ghostObject, isValid);
        }
    }

    private void ApplyGhostMaterial(GameObject obj, bool isValid)
    {
        Material matToUse = isValid ? ghostMaterialValid : ghostMaterialInvalid;

        foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
        {
            renderer.material = matToUse;
        }

        foreach (var collider in obj.GetComponentsInChildren<Collider>())
        {
            collider.enabled = false;
        }
    }

    public void SetPlotID(string id)
    {
        Debug.Log($"Assigned Plot ID: {id}");
    }

    public void SetUIReferences(GameObject panel, TMP_Text infoText, Button upgradeBtn)
    {
        selectedCuboidUIPanel = panel;
        selectedCuboidInfoText = infoText;
        upgradeButton = upgradeBtn;
    }

    public void HighlightPlot(Color color)
    {
        currentHighlightColor = color;

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                tileGrid[x, z]?.SetPersistentColor(color);
            }
        }
    }

    public void ResetTileColors()
    {
        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                tileGrid[x, z]?.ClearHighlight();
            }
        }
    }

    public void SetButtonSelector(BuildingButtonSelector selector)
    {
        buttonSelector = selector;
    }

    public void SetEditMode(bool state)
    {
        isEditMode = state;

        if (!state)
        {
            ClearSelectionAndUI();
        }
    }

    private void ClearSelectionAndUI()
    {
        hasSelectedCuboid = false;

        if (selectedCuboidUIPanel != null)
            selectedCuboidUIPanel.SetActive(false);

        if (upgradeButton != null)
            upgradeButton.gameObject.SetActive(false);

        ClearHighlights();
    }

    public bool IsInEditMode()
    {
        return isEditMode;
    }

    public void HideCuboidInfo()
    {
        selectedCuboidUIPanel?.SetActive(false);
        upgradeButton?.gameObject.SetActive(false);
        SelectableCuboid.currentlySelectedCuboid = null;
    }
}
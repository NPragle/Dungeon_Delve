using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    [Header("Ability configuration")]
    [Tooltip("Names of the prefabs stored in Resources to spawn for each ability. " +
             "You can leave slots empty and they will be ignored. Slot 0 = key 1 (default Fireball), slot 1 = key 2, etc.")]
    [SerializeField]
    // the first slot is preconfigured for a Fireball prefab; make sure a Resources/Fireball prefab exists
    private string[] abilityResourceNames = new string[4] { "Fireball", "", "", "" };

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // could perform runtime validation here if desired
    }

    // Update is called once per frame
    void Update()
    {
        // legacy key for one projectile
        if (Input.GetKeyDown(KeyCode.E))
        {
            UseAbilityByName("Wizard Can");
        }

        // check number keys 1-4 and use corresponding ability
        if (Input.GetKeyDown(KeyCode.Alpha1))
            TryUseAbility(0);
        if (Input.GetKeyDown(KeyCode.Alpha2))
            TryUseAbility(1);
        if (Input.GetKeyDown(KeyCode.Alpha3))
            TryUseAbility(2);
        if (Input.GetKeyDown(KeyCode.Alpha4))
            TryUseAbility(3);
    }

    private void TryUseAbility(int slot)
    {
        if (slot < 0 || slot >= abilityResourceNames.Length)
            return;
        var name = abilityResourceNames[slot];
        if (string.IsNullOrWhiteSpace(name))
            return;
        UseAbilityByName(name);
    }

    private void UseAbilityByName(string resourceName)
    {
        var prefab = Resources.Load<GameObject>(resourceName);
        if (prefab == null)
        {
            Debug.LogWarning($"Prefab '{resourceName}' not found in a Resources folder.");
            return;
        }

        var projectile = Instantiate(prefab);
        projectile.transform.position = transform.position + transform.forward * 1.0f + Vector3.up * 0.5f;
        projectile.transform.localScale = Vector3.one * 0.3f;

        var rb = projectile.AddComponent<Rigidbody>();
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        float shootSpeed = 20f;
        rb.linearVelocity = transform.forward * shootSpeed;

        Destroy(projectile, 5f);
    }
}

using UnityEngine;

namespace Assets.Scripts
{
    public class Cube : MonoBehaviour
    {
        private int globalNumber = 10;

        private void Start()
        {
            int localNumber = 5;
            Debug.Log($"Global variable: {globalNumber}");
            Debug.Log($"Local variable: {localNumber}");

            PassByValue(localNumber);
            Debug.Log($"After PassByValue: {localNumber}");

            PassByReference(ref localNumber);
            Debug.Log($"After PassByReference: {localNumber}");

            GameObject sphere = GameObject.Find("Sphere");
            if (sphere != null)
            {
                Debug.Log($"Found object: {sphere.name}");
            }
        }

        private void PassByValue(int value)
        {
            value += 10;
            Debug.Log($"Inside PassByValue: {value}");
        }

        private void PassByReference(ref int value)
        {
            value += 10;
            Debug.Log($"Inside PassByReference: {value}");
        }
    }
}

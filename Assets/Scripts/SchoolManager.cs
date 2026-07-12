using System;
using UnityEngine;

namespace Assets.Scripts
{
    public class SchoolManager : MonoBehaviour
    {
        [Serializable]
        public class Person
        {
            public string name;
            public int age;
        }

        [Header("Student Information")]
        public Person student = new Person();

        [Space(10)]
        [Range(0, 10)]
        public int score = 5;

        [TextArea(3, 5)]
        public string note;

        [Tooltip("Demo field for Lab inspector attributes.")]
        public string tooltipDemo;
    }
}

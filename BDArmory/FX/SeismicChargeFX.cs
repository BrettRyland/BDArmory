using System;
using BDArmory.Extensions;
using BDArmory.Utils;
using UnityEngine;

namespace BDArmory.FX
{
    public class SeismicChargeFX : MonoBehaviour
    {
        AudioSource audioSource;

        public static float originalShipVolume;
        public static float originalMusicVolume;
        public static float originalAmbienceVolume;

        float startTime;

        Transform lightFlare;

        Rigidbody rb;

        void Start()
        {
            transform.localScale = 2 * Vector3.one;
            lightFlare = gameObject.transform.Find("lightFlare");

            startTime = Time.time;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.minDistance = 5000;
            audioSource.maxDistance = 5000;
            audioSource.dopplerLevel = 0f;
            audioSource.pitch = UnityEngine.Random.Range(0.93f, 1f);
            audioSource.volume = BDAMath.Sqrt(originalShipVolume);

            audioSource.PlayOneShot(SoundUtils.GetAudioClip("BDArmory/Sounds/seismicCharge"));

            rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.velocity = Vector3.zero;
        }

        void FixedUpdate()
        {
            lightFlare.LookAt(FlightCamera.fetch.mainCamera.transform.position,
                FlightCamera.fetch.mainCamera.transform.up);

            if (Time.time - startTime < 1.25f)
            {
                //
                GameSettings.SHIP_VOLUME = Mathf.MoveTowards(GameSettings.SHIP_VOLUME, 0, originalShipVolume / 0.7f);
                GameSettings.MUSIC_VOLUME = Mathf.MoveTowards(GameSettings.MUSIC_VOLUME, 0, originalShipVolume / 0.7f);
                GameSettings.AMBIENCE_VOLUME = Mathf.MoveTowards(GameSettings.AMBIENCE_VOLUME, 0,
                    originalShipVolume / 0.7f);
            }
            else if (Time.time - startTime < 7.35f / audioSource.pitch)
            {
                //make it fade in more slowly
                GameSettings.SHIP_VOLUME = Mathf.MoveTowards(GameSettings.SHIP_VOLUME, originalShipVolume,
                    originalShipVolume / 3f * Time.fixedDeltaTime);
                GameSettings.MUSIC_VOLUME = Mathf.MoveTowards(GameSettings.MUSIC_VOLUME, originalMusicVolume,
                    originalMusicVolume / 3f * Time.fixedDeltaTime);
                GameSettings.AMBIENCE_VOLUME = Mathf.MoveTowards(GameSettings.AMBIENCE_VOLUME, originalAmbienceVolume,
                    originalAmbienceVolume / 3f * Time.fixedDeltaTime);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnTriggerEnter(Collider other)
        {
            //hitting parts
            Part explodePart = null;
            try
            {
                explodePart = other.gameObject.GetComponentUpwards<Part>();
                explodePart.Unpack();
            }
            catch (NullReferenceException e)
            {
                Debug.LogWarning("[BDArmory.SeismicChargeFX]: Exception thrown in OnTriggerEnter: " + e.Message + "\n" + e.StackTrace);
            }

            if (explodePart != null)
            {
                explodePart.Destroy();
            }
            else
            {
                //hitting buildings
                DestructibleBuilding hitBuilding = null;
                try
                {
                    hitBuilding = other.gameObject.GetComponentUpwards<DestructibleBuilding>();
                }
                catch (NullReferenceException e)
                {
                    Debug.LogWarning("[BDArmory.SeismicChargeFX]: Exception thrown in OnTriggerEnter: " + e.Message + "\n" + e.StackTrace);
                }
                if (hitBuilding != null && hitBuilding.IsIntact)
                {
                    hitBuilding.Demolish();
                }
            }
            Destroy(gameObject);
        }

        public static void CreateSeismicExplosion(Vector3 pos, Quaternion rot)
        {
            GameObject explosionModel = GameDatabase.Instance.GetModel("BDArmory/Models/seismicCharge/seismicExplosion");
            GameObject explosionObject = (GameObject)Instantiate(explosionModel, pos, rot);
            explosionObject.SetActive(true);
            explosionObject.AddComponent<SeismicChargeFX>();
        }
    }
}

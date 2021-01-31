using BDArmory.Misc;
using BDArmory.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BDArmory.FX
{
	class FuelLeakFX : MonoBehaviour
	{
		Part parentPart;
		public bool Emit;
		public float MaxDistance = 1.1f;
		public Part part;
		public KSPParticleEmitter PEmitter;
		public Rigidbody rb;
		Vector3 internalVelocity;
		Vector3 lastPos;
		Vector3 velocity
		{
			get
			{
				if (rb)
				{
					return rb.velocity;
				}
				else if (part)
				{
					return part.rb.velocity;
				}
				else
				{
					return internalVelocity;
				}
			}
		}
		public static ObjectPool CreateLeakFXPool(string modelPath)
		{
			var template = GameDatabase.Instance.GetModel(modelPath);
			var decal = template.AddComponent<FuelLeakFX>();
			template.SetActive(false);
			return ObjectPool.CreateObjectPool(template, 10, true, true);
		}

		public float lifeTime = 20;
		private float startTime;
		private float disableTime = -1;
		private float _highestEnergy = 1;
		KSPParticleEmitter[] pEmitters;
		void OnEnable()
		{
			BDArmorySetup.numberOfParticleEmitters++;
			startTime = Time.time;
			pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

			using (var pe = pEmitters.AsEnumerable().GetEnumerator())
				while (pe.MoveNext())
				{
					if (pe.Current == null) continue;

					pe.Current.force = FlightGlobals.getGeeForceAtPosition(transform.position);
					pe.Current.emit = true;
					_highestEnergy = pe.Current.maxEnergy;
					EffectBehaviour.AddParticleEmitter(pe.Current);
				}
			MaxDistance = PEmitter.minSize / 3;
			lastPos = transform.position;
		}
		void onDisable()
		{
			BDArmorySetup.numberOfParticleEmitters--;
			foreach (var pe in pEmitters)
				if (pe != null)
				{
					pe.emit = false;
					EffectBehaviour.RemoveParticleEmitter(pe);
				}
		}
		void Update()
		{
			if (!gameObject.activeInHierarchy)
			{
				return;
			}
			if (Time.time - startTime >= lifeTime && disableTime < 0)
			{
				disableTime = Time.time; //grab time when emission stops
				foreach (var pe in pEmitters)
					if (pe != null)
						pe.emit = false;
			}
			if (disableTime > 0 && Time.time - disableTime > _highestEnergy) //wait until last emitted particle has finished
			{
				gameObject.SetActive(false);
			}
			using (var pe = pEmitters.AsEnumerable().GetEnumerator())
				while (pe.MoveNext())
				{
					if (pe.Current == null) continue;
					pe.Current.force = FlightGlobals.getGeeForceAtPosition(transform.position);
				}
		}
		public void AttachAt(Part hitPart, RaycastHit hit, Vector3 offset)
		{
			parentPart = hitPart;
			transform.SetParent(hitPart.transform);
			transform.position = hit.point + offset;
			parentPart.OnJustAboutToDie += OnParentDestroy;
			parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
			gameObject.SetActive(true);
		}
		public void OnParentDestroy()
		{
			if (parentPart)
			{
				parentPart.OnJustAboutToDie -= OnParentDestroy;
				parentPart.OnJustAboutToBeDestroyed -= OnParentDestroy;
				parentPart = null;
				transform.parent = null;
				gameObject.SetActive(false);
			}
		}
		public void OnDestroy()
		{
			OnParentDestroy();
		}
		private void FixedUpdate()
		{
			if (!part && !rb)
			{
				internalVelocity = (transform.position - lastPos) / Time.fixedDeltaTime;
				lastPos = transform.position;
				if (PEmitter.emit && internalVelocity.sqrMagnitude > 562500)
				{
					return; //dont bridge gap if floating origin shifted
				}
			}

			if (!Emit) return;
			if (!gameObject.activeInHierarchy) return;

			//var velocity = part?.GetComponent<Rigidbody>().velocity ?? rb.velocity;
			var originalLocalPosition = gameObject.transform.localPosition;
			var originalPosition = gameObject.transform.position;
			var startPosition = gameObject.transform.position + velocity * Time.fixedDeltaTime;
			var originalGapDistance = Vector3.Distance(originalPosition, startPosition);
			var intermediateSteps = originalGapDistance / MaxDistance;

			PEmitter.EmitParticle();
			gameObject.transform.position = Vector3.MoveTowards(
				gameObject.transform.position,
				startPosition,
				MaxDistance);
			for (var i = 1; i < intermediateSteps; i++)
			{
				PEmitter.EmitParticle();
				gameObject.transform.position = Vector3.MoveTowards(
				gameObject.transform.position,
				startPosition,
				MaxDistance);
			}
			gameObject.transform.localPosition = originalLocalPosition;
		}

		public void EmitParticles()
		{
			var velocity = part?.GetComponent<Rigidbody>().velocity ?? rb.velocity;
			var originalLocalPosition = gameObject.transform.localPosition;
			var originalPosition = gameObject.transform.position;
			var startPosition = gameObject.transform.position + velocity * Time.fixedDeltaTime;
			var originalGapDistance = Vector3.Distance(originalPosition, startPosition);
			var intermediateSteps = originalGapDistance / MaxDistance;

			//gameObject.transform.position = startPosition;
			for (var i = 0; i < intermediateSteps; i++)
			{
				PEmitter.EmitParticle();
				gameObject.transform.position = Vector3.MoveTowards(
				gameObject.transform.position,
				startPosition,
				MaxDistance);
			}
			gameObject.transform.localPosition = originalLocalPosition;
		}
	}
}

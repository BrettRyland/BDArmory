using BDArmory.Competition;
using BDArmory.Control;
using BDArmory.Core;
using BDArmory.Core.Extension;
using BDArmory.Core.Utils;
using BDArmory.Misc;
using BDArmory.UI;
using System.Linq;
using UnityEngine;

namespace BDArmory.FX
{
	class FireFX : MonoBehaviour
	{
		Part parentPart;
		public static ObjectPool CreateFireFXPool(string modelPath)
		{
			var template = GameDatabase.Instance.GetModel(modelPath);
			var decal = template.AddComponent<FireFX>();
			template.SetActive(false);
			return ObjectPool.CreateObjectPool(template, 10, true, true);
		}

		private float disableTime = -1;
		private float _highestEnergy = 1;
		public float burnTime = -1;
		private float startTime;
		public bool hasFuel = true;
		public float burnRate = 1;
		private float tntMassEquivilent = 0;
		float ScoreAccumulator = 0;
		private string SourceVessel;
		private string explModelPath = "BDArmory/Models/explosion/explosion";
		private string explSoundPath = "BDArmory/Sounds/explode1";

		KSPParticleEmitter[] pEmitters;
		void OnEnable()
		{
			hasFuel = true;
			startTime = Time.time;
			foreach (var existingLeakFX in parentPart.GetComponentsInChildren<FuelLeakFX>())
			{
				existingLeakFX.lifeTime = 0; //kill leak FX
			}
			BDArmorySetup.numberOfParticleEmitters++;
			pEmitters = gameObject.GetComponentsInChildren<KSPParticleEmitter>();

			using (var pe = pEmitters.AsEnumerable().GetEnumerator())
				while (pe.MoveNext())
				{
					if (pe.Current == null) continue;
					pe.Current.force = -FlightGlobals.getGeeForceAtPosition(transform.position)/2;
					pe.Current.emit = true;
					_highestEnergy = pe.Current.maxEnergy;
					EffectBehaviour.AddParticleEmitter(pe.Current);
				}
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
			if (!gameObject.activeInHierarchy || !HighLogic.LoadedSceneIsFlight)
			{
				return;
			}
			PartResource fuel = parentPart.Resources.Where(pr => pr.resourceName == "LiquidFuel").FirstOrDefault();
			var engine = parentPart.FindModuleImplementing<ModuleEngines>();
			if (engine != null)
			{
				if (engine.enabled)
				{
					if (parentPart.RequestResource("LiquidFuel", burnRate * Time.fixedDeltaTime) <= 0)
					{
						hasFuel = false;
					}
				}
			}
			else
			{
				if (fuel != null)
				{
					if (fuel.amount > (fuel.maxAmount * 0.15f) || (fuel.amount > 0 && fuel.amount < (fuel.maxAmount * 0.10f)))
					{
						fuel.amount -= (burnRate * TimeWarp.deltaTime);
					}
					else if (fuel.amount < (fuel.maxAmount * 0.15f) && fuel.amount > (fuel.maxAmount * 0.10f))
					{
						tntMassEquivilent += (float)fuel.amount;
						Detonate();
					}
					else
					{
						hasFuel = false;
					}
				}
				PartResource ox = parentPart.Resources.Where(pr => pr.resourceName == "Oxidizer").FirstOrDefault();
				if (ox != null)
				{
					if (ox.amount > 0)
					{
						ox.amount -= (burnRate * TimeWarp.deltaTime);
					}
					else
					{
						hasFuel = false;
					}
				}
				PartResource mp = parentPart.Resources.Where(pr => pr.resourceName == "MonoPropellant").FirstOrDefault();
				if (mp != null)
				{
					if (mp.amount > (mp.maxAmount * 0.15f) || (mp.amount > 0 && mp.amount < (mp.maxAmount * 0.10f)))
					{
						mp.amount -= (burnRate * TimeWarp.deltaTime);
					}
					else if (mp.amount < (mp.maxAmount * 0.15f) && mp.amount > (mp.maxAmount * 0.10f))
					{
						Detonate();
					}
					else
					{
						hasFuel = false;
					}
				}
				PartResource ec = parentPart.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
				if (ec != null)
				{
					if (ec.amount > 0)
					{
						ec.amount -= (burnRate * TimeWarp.deltaTime);
						Mathf.Clamp((float)ec.amount, 0, Mathf.Infinity);
					}
					if ((Time.time - startTime > 30) && engine == null)
					{
						Detonate();
					}
				}
			}
			if ((!hasFuel && disableTime < 0 && burnTime < 0) || (burnTime > 0 && disableTime < 0 && Time.time - startTime > burnTime))
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
			if (BDArmorySettings.BD_FIRE_DOT)
			{
				parentPart.AddDamage(BDArmorySettings.BD_FIRE_DAMAGE*TimeWarp.deltaTime);
				////////////////////////////////////////////////
				if (ScoreAccumulator >= 1)
				{
					ScoreAccumulator = 0;
					var aName = SourceVessel;
					var tName = parentPart.vessel.GetName(); 

					if (aName != null && tName != null && aName != tName && BDACompetitionMode.Instance.Scores.ContainsKey(aName) && BDACompetitionMode.Instance.Scores.ContainsKey(tName))
					{
						if (BDArmorySettings.REMOTE_LOGGING_ENABLED)
						{
							BDAScoreService.Instance.TrackDamage(aName, tName, BDArmorySettings.BD_FIRE_DAMAGE);
						}
						var aData = BDACompetitionMode.Instance.Scores[aName];
						aData.Score += 1;

						if (parentPart.vessel.GetName() == "Pinata")
						{
							aData.PinataHits++;
						}

						var tData = BDACompetitionMode.Instance.Scores[tName];
						tData.lastPersonWhoHitMe = aName;
						tData.lastHitTime = Planetarium.GetUniversalTime();
						tData.everyoneWhoHitMe.Add(aName);
						// Track hits
						if (tData.hitCounts.ContainsKey(aName))
							++tData.hitCounts[aName];
						else
							tData.hitCounts.Add(aName, 1);
						// Track damage
						if (tData.damageFromBullets.ContainsKey(aName))
							tData.damageFromBullets[aName] += BDArmorySettings.BD_FIRE_DAMAGE;
						else
							tData.damageFromBullets.Add(aName, BDArmorySettings.BD_FIRE_DAMAGE);

					}
				}
				else
				{
					ScoreAccumulator += 1 * TimeWarp.deltaTime;
				}
			}
			////////////////////////////////////////////
			using (var pe = pEmitters.AsEnumerable().GetEnumerator())
				while (pe.MoveNext())
				{
					if (pe.Current == null) continue;
					pe.Current.force = -FlightGlobals.getGeeForceAtPosition(transform.position) / 2;
				}
		}
		void Detonate()
		{
			if (!parentPart.partName.Contains("exploding"))
			{
				bool excessFuel = false;
				parentPart.partName += "exploding";
				PartResource fuel = parentPart.Resources.Where(pr => pr.resourceName == "LiquidFuel").FirstOrDefault();
				PartResource ox = parentPart.Resources.Where(pr => pr.resourceName == "Oxidizer").FirstOrDefault();
				if (fuel != null)
				{
					tntMassEquivilent += Mathf.Clamp((float)fuel.amount, ((float)fuel.maxAmount * 0.05f), ((float)fuel.maxAmount * 0.2f));
					if (fuel != null && ox != null)
					{
						tntMassEquivilent += Mathf.Clamp((float)ox.amount, ((float)ox.maxAmount * 0.1f), ((float)ox.maxAmount * 0.3f));
						tntMassEquivilent *= 1.3f;
					}
					if (fuel.amount > fuel.maxAmount * 0.3f)
					{
						excessFuel = true;
					}
				}
				PartResource mp = parentPart.Resources.Where(pr => pr.resourceName == "MonoPropellant").FirstOrDefault();
				if (mp != null)
				{
					tntMassEquivilent += Mathf.Clamp((float)mp.amount, ((float)mp.maxAmount * 0.1f), ((float)mp.maxAmount * 0.3f));
					if (mp.amount > mp.maxAmount * 0.3f)
					{
						excessFuel = true;
					}
				}
				PartResource ec = parentPart.Resources.Where(pr => pr.resourceName == "ElectricCharge").FirstOrDefault();
				if (ec != null)
				{
					tntMassEquivilent += (parentPart.mass / 80);
					ec.maxAmount = 0;
					ec.isVisible = false;
					parentPart.RemoveResource(ec);//destroy battery. not calling part.destroy, since some batteries in cockpits.
					Misc.Misc.RefreshAssociatedWindows(parentPart);
				}
				if (excessFuel)
				{
					float blastRadius = BlastPhysicsUtils.CalculateBlastRange(tntMassEquivilent);
					using (var blastHits = Physics.OverlapSphere(parentPart.transform.position, blastRadius, 9076737).AsEnumerable().GetEnumerator())
					{
						while (blastHits.MoveNext())
						{
							if (blastHits.Current == null) continue;
							try
							{
								Part partHit = blastHits.Current.GetComponentInParent<Part>();
								if (partHit != null && partHit.mass > 0)
								{
									Rigidbody rb = partHit.Rigidbody;
									Vector3 distToG0 = parentPart.transform.position - partHit.transform.position;

									Ray LoSRay = new Ray(parentPart.transform.position, partHit.transform.position - parentPart.transform.position);
									RaycastHit hit;
									if (Physics.Raycast(LoSRay, out hit, distToG0.magnitude, 9076737))
									{
										KerbalEVA eva = hit.collider.gameObject.GetComponentUpwards<KerbalEVA>();
										Part p = eva ? eva.part : hit.collider.gameObject.GetComponentInParent<Part>();
										if (p == partHit)
										{
											if (rb == null) return;
											BulletHitFX.AttachFire(hit, p, 1, SourceVessel, 20);
										}
									}
								}
							}
							catch
							{
							}
						}
					}
				}
				ExplosionFx.CreateExplosion(parentPart.transform.position, tntMassEquivilent, explModelPath, explSoundPath, ExplosionSourceType.Bullet, 0, null, parentPart.vessel.name);
				// needs to be Explosiontype Bullet since missile only returns Module MissileLauncher
				gameObject.SetActive(false);
			}
		}
		public void AttachAt(Part hitPart, RaycastHit hit, Vector3 offset, string sourcevessel, float burnTime = -1)
		{
			parentPart = hitPart;
			transform.SetParent(hitPart.transform);
			transform.position = hit.point + offset;			
			parentPart.OnJustAboutToDie += OnParentDestroy;
			parentPart.OnJustAboutToBeDestroyed += OnParentDestroy;
			SourceVessel = sourcevessel;
			gameObject.SetActive(true);
		}
		public void OnParentDestroy()
		{
			if (parentPart)
			{
				parentPart.OnJustAboutToDie -= OnParentDestroy;
				parentPart.OnJustAboutToBeDestroyed -= OnParentDestroy;
				Detonate();
				parentPart = null;
				transform.parent = null;
				gameObject.SetActive(false);
			}
		}
		public void OnDestroy()
		{
			OnParentDestroy();
		}
	}
}

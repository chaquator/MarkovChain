using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using System.Diagnostics;

namespace MarkovChain {
	namespace Structs {
		/// <summary>
		/// Master MarkovStructure, has associated dictioanry, array of links,
		/// and an array of indeces to links array which are all "starers"
		/// </summary>
		public class MarkovStructure {
			/// <summary>
			/// Dictionary is an array of strings, where each index is used as the reference in the n-grams
			/// </summary>
			public string[] dictionary;

			/// <summary>
			/// Grams is an array of all possible unqiue grams on their own
			/// </summary>
			public readonly NGram[] grams;

			/// <summary>
			/// Array of all unique ngrams, each with their successors
			/// </summary>
			public readonly MarkovSegment[] chain_links;

			/// <summary>
			/// Array of indeces which point to chain links that happen to be starts of sentences
			/// </summary>
			public readonly int[] seeds;

			// TODO: WRITE
			public int[] GenerateSqeuence() {
				return new int[] { 0 };
			}

			// TODO: write
			public string SequenceToString(int[] seq) {
				return "";
			}

			// TODO: summary
			public string GenerateSentence() {
				return SequenceToString(GenerateSqeuence());
			}

			public void WriteFile(string filename) {
				JsonWriterOptions jswropt = new JsonWriterOptions {
					Indented = false,
					SkipValidation = true
				};
				JsonSerializerOptions jssropt = new JsonSerializerOptions() {
					WriteIndented = false
				};

				using (FileStream fs = new FileStream(filename, FileMode.Create))
				using (Utf8JsonWriter jswr = new Utf8JsonWriter(fs, jswropt)) {
					MarkovStructureJsonConverter converter = new MarkovStructureJsonConverter();
					converter.Write(jswr, this, jssropt);
				}
			}

			public override string ToString() {
				JsonSerializerOptions jssropt = new JsonSerializerOptions() {
					WriteIndented = false
				};
				jssropt.Converters.Add(new MarkovStructureJsonConverter());
				return JsonSerializer.Serialize<MarkovStructure>(this, jssropt);
			}

			public static MarkovStructure ReadFile(string filename) {
				using (FileStream fs = new FileStream(filename, FileMode.Open)) {
					JsonSerializerOptions jssropt = new JsonSerializerOptions();
					jssropt.Converters.Add(new MarkovStructureJsonConverter());
					return JsonSerializer.DeserializeAsync<MarkovStructure>(fs, jssropt).Result;
				}
			}

			/// <summary>
			/// Constructor for a markovstructure, only to be used by pipeline
			/// </summary>
			/// <param name="dic">Master dictionary, array of words</param>
			/// <param name="grms">Thread-safe master list of ngrams</param>
			/// <param name="prototype_chainlinks">Prototype of chain links, maps ngram-index
			/// to prototype of successors (which maps succeeding index to weight)</param>
			/// <param name="sds">Prototype of seed list, in hash map form for quick access</param>
			public MarkovStructure(string[] dic, ConcurrentQueue<NGram> grms,
								ConcurrentDictionary<int, ConcurrentDictionary<int, int>> prototype_chainlinks,
								ConcurrentDictionary<int, bool> sds) {
				// Pass along master dictionary
				dictionary = dic;

				// Populate master grams table 
				grams = grms.ToArray();

				// Populate chain links
				chain_links = new MarkovSegment[grams.Length];
				Parallel.For(0, grams.Length,
					(ind) => {
						chain_links[ind] = new MarkovSegment(ind, prototype_chainlinks[ind]);
					}
				);
				//for (int ind = 0; ind < grams.Length; ++ind) {
				//	chain_links[ind] = new MarkovSegment(ind, prototype_chainlinks[ind]);
				//}

				// Populate list of seeds
				seeds = sds.Keys.ToArray();
			}

			public MarkovStructure(string[] dic, NGram[] grms, MarkovSegment[] links, int[] sds) {
				dictionary = dic;
				grams = grms;
				chain_links = links;
				seeds = sds;
			}
		}

		/// <summary>
		/// Single segment in overall MarkovStructure, used in tandem with master
		/// array to assemble sentence
		/// </summary>
		public class MarkovSegment {
			/// <summary>
			/// Index which points to associated ngram in master
			/// markov structure
			/// </summary>
			public readonly int current_ngram;

			/// <summary>
			/// Sorted array of ngrams which succeed given ngram, along with their relatively frequency
			/// </summary>
			public readonly NGramSuccessor[] successors;

			/// <summary>
			/// Array which is used for random selection which contains running total of weights
			/// for each index in the successors
			/// </summary>
			public readonly int[] runningTotal;

			/// <summary>
			/// Selects random successor paying attention to weight
			/// </summary>
			/// <remarks>Hopscotch selection from https://blog.bruce-hill.com/a-faster-weighted-random-choice</remarks>
			/// <returns></returns>
			public int randomSuccessor() {
				// TODO
			}

			/// <summary>
			/// Constructor of MarkovSegment, meant to be used by MarkovStructure
			/// </summary>
			/// <param name="ngram"></param>
			/// <param name="prototype_successors"></param>
			public MarkovSegment(int ngram, ConcurrentDictionary<int, int> prototype_successors) {
				// key is index of current ngram
				// value is map<index of successor, associated weight>
				current_ngram = ngram;

				// Populate successors, running total
				List<NGramSuccessor> sucset = new List<NGramSuccessor>(prototype_successors.Count);
				runningTotal = new int[prototype_successors.Count];

				int total_weight = 0;
				NGramSuccessor add;
				int bs; // index to binary search for
				foreach (var successor in prototype_successors) {
					total_weight += successor.Value;

					add = new NGramSuccessor(successor.Key, successor.Value);

					bs = sucset.BinarySearch(add);
					bs = (bs == -1) ? 0 : bs;

					sucset.Insert(bs, add);
					runningTotal[bs] = total_weight;
				}

				successors = sucset.ToArray();
			}

			public MarkovSegment(int ngram, NGramSuccessor[] sucs, int[] runtot) {
				current_ngram = ngram;
				successors = sucs;
				runningTotal = runtot;
			}
		}

		/// <summary>
		/// Successor struct, couples ngram
		/// </summary>
		public struct NGramSuccessor : IComparable<NGramSuccessor> {
			/// <summary>
			/// Index points to associated MarkovStructure chain_links index
			/// </summary>
			public readonly int successor_index;

			/// <summary>
			/// Weight whos magnitude reflects relative frequency of successor
			/// </summary>
			public readonly int weight;

			public NGramSuccessor(int suc, int w) {
				successor_index = suc;
				weight = w;
			}

			int IComparable<NGramSuccessor>.CompareTo(NGramSuccessor o) {
				return weight.CompareTo(o.weight);
			}
		}

		/// <summary>
		/// Individual ngram
		/// </summary>
		public struct NGram {
			/// <summary>
			/// Array of indeces which correspond to words in dictionary
			/// </summary>
			public readonly int[] gram;

			public NGram(int[] g) {
				gram = g;
			}

			public override bool Equals(object obj) {
				if ((obj == null) || !GetType().Equals(obj.GetType())) {
					return false;
				} else {
					NGram o = (NGram)obj;

					// Compare by array value, not by reference or whatever C# does by default
					return Enumerable.SequenceEqual(gram, o.gram);
				}
			}

			public override int GetHashCode() {
				unchecked {
					int SEED = (int)2509506049;
					int LARGEPRIME = (int)4134118063;

					return gram.Aggregate(SEED, (hash, field) => (LARGEPRIME * hash ^ field.GetHashCode()));
				}
			}

			public override string ToString() {
				return String.Join(" ", gram);
			}
		}

		public class MarkovStructureJsonConverter : JsonConverter<MarkovStructure> {
			public override bool CanConvert(Type typeToConvert) {
				return true;
			}

			public override MarkovStructure Read(ref Utf8JsonReader reader,
				Type typeToConvert,
				JsonSerializerOptions options) {

				List<string> dictionary = new List<string>();

				List<NGram> grams = new List<NGram>();
				List<int> curgram;

				List<MarkovSegment> links = new List<MarkovSegment>();
				int current_cur;
				List<NGramSuccessor> current_succesors;
				List<int> current_running;
				int current_sucind;
				int current_weight;

				List<int> seeds = new List<int>();

				// TODO: maybe do something about debug asserts??
				void readDic(ref Utf8JsonReader r) {
					// Start array
					r.Read();
					Debug.Assert(r.TokenType == JsonTokenType.StartArray);

					// Loop must begin where token to be is string
					while (true) {
						r.Read();
						if (r.TokenType != JsonTokenType.String) break;

						dictionary.Add(r.GetString());
					}

					// Ends ready to parse next property
				}

				void readNGramsWhole(ref Utf8JsonReader r) {
					// Start array
					r.Read();
					Debug.Assert(r.TokenType == JsonTokenType.StartArray);

					// Loop must begin where token to be is start of array
					while (true) {
						// Start of ngram array
						r.Read();

						if (r.TokenType != JsonTokenType.StartArray) break; // break at end of array

						// Read NGram (array of ints)
						curgram = new List<int>();
						while (true) {
							r.Read();
							if (r.TokenType != JsonTokenType.Number) break; // break at end of array

							curgram.Add(r.GetInt32());
						}
						grams.Add(new NGram(curgram.ToArray()));
					}

					// Ends ready to parse next property
				}

				void readMarkovSegmentsWhole(ref Utf8JsonReader r) {
					// Start of links array
					r.Read();
					Debug.Assert(r.TokenType == JsonTokenType.StartArray);

					// Loop must begin where token to be is start of object
					while (true) {
						r.Read();

						// Exit when at end of array (token isnt start of object)
						if (r.TokenType != JsonTokenType.StartObject) break;

						// Current index property name
						r.Read();
						Debug.Assert(r.TokenType == JsonTokenType.PropertyName);

						// Current index value
						r.Read();
						current_cur = r.GetInt32();

						// Parse successors

						// Successor property name
						r.Read();
						Debug.Assert(r.TokenType == JsonTokenType.PropertyName);

						// Start of successors array
						r.Read();
						Debug.Assert(r.TokenType == JsonTokenType.StartArray);

						current_succesors = new List<NGramSuccessor>();

						// Loop must begin where token to be is start of object
						while (true) {
							r.Read();

							// Exit at end of array (is not start object)
							if (r.TokenType != JsonTokenType.StartObject) break;

							// Index property name
							r.Read();
							Debug.Assert(r.TokenType == JsonTokenType.PropertyName);

							// Index value
							r.Read();
							current_sucind = r.GetInt32();

							// Weight property name
							r.Read();
							Debug.Assert(r.TokenType == JsonTokenType.PropertyName);

							// Weight value
							r.Read();
							current_weight = r.GetInt32();

							// End of NGramSuccessor object
							r.Read();
							Debug.Assert(r.TokenType == JsonTokenType.EndObject);

							current_succesors.Add(new NGramSuccessor(current_sucind, current_weight));
						}

						// Parse running totals

						// Running totals name
						r.Read();
						Debug.Assert(r.TokenType == JsonTokenType.PropertyName);

						// Start of running totals array
						r.Read();
						Debug.Assert(r.TokenType == JsonTokenType.StartArray);

						current_running = new List<int>();

						// Loop must begin where token to be is start of object
						while (true) {
							r.Read();

							if (r.TokenType != JsonTokenType.Number) break;

							// Read integer
							r.Read();
							current_running.Add(r.GetInt32());
						}

						// End of MarkovSegment object
						r.Read();

						links.Add(new MarkovSegment(current_cur, current_succesors.ToArray(), current_running.ToArray()));
					}

					// Ends ready to parse next property
				}

				void readSeeds(ref Utf8JsonReader r) {
					// Start of array
					r.Read();
					Debug.Assert(r.TokenType == JsonTokenType.StartArray);

					while (true) {
						r.Read();

						// Exit at end of array (not number)
						if (r.TokenType != JsonTokenType.Number) break;

						seeds.Add(r.GetInt32());
					}

					// Ends ready to parse next property
				}

				// Read order indiscriminately, ignoring comments & other things
				while (reader.Read()) {
					if (reader.TokenType == JsonTokenType.PropertyName) {
						switch (reader.GetString()) {
							case "dictionary":
								readDic(ref reader);
								break;
							case "ngrams":
								readNGramsWhole(ref reader);
								break;
							case "links":
								readMarkovSegmentsWhole(ref reader);
								break;
							case "seeds":
								readSeeds(ref reader);
								break;
						}
					}
				}

				return new MarkovStructure(
					dictionary.ToArray(),
					grams.ToArray(),
					links.ToArray(),
					seeds.ToArray());
			}

			public override void Write(Utf8JsonWriter writer,
				MarkovStructure value,
				JsonSerializerOptions options) {

				writer.WriteStartObject();

				// Dictionary
				writer.WriteStartArray("dictionary");
				foreach (string word in value.dictionary) {
					writer.WriteStringValue(word);
				}
				writer.WriteEndArray();

				// NGrams dictionary 
				writer.WriteStartArray("ngrams");
				foreach (NGram gram in value.grams) {
					writer.WriteStartArray();
					foreach (int w in gram.gram) {
						writer.WriteNumberValue(w);
					}
					writer.WriteEndArray();
				}
				writer.WriteEndArray();

				// Chain links
				writer.WriteStartArray("links");
				foreach (MarkovSegment link in value.chain_links) {
					writer.WriteStartObject();

					// Current ngram
					writer.WriteNumber("current", link.current_ngram);

					// Successors
					writer.WriteStartArray("successors");
					foreach (NGramSuccessor suc in link.successors) {
						writer.WriteStartObject();

						// Index
						writer.WriteNumber("index", suc.successor_index);

						// Weight
						writer.WriteNumber("weight", suc.weight);

						writer.WriteEndObject();
					}
					writer.WriteEndArray();

					// Running total array
					writer.WriteStartArray("running");
					foreach (int cur in link.runningTotal) {
						writer.WriteNumberValue(cur);
					}
					writer.WriteEndArray();

					writer.WriteEndObject();
				}
				writer.WriteEndArray();

				// Seeds
				writer.WriteStartArray("seeds");
				foreach (int s in value.seeds) {
					writer.WriteNumberValue(s);
				}
				writer.WriteEndArray();

				writer.WriteEndObject();
			}
		}
	}
}

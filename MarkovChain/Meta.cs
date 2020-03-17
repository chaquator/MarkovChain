using System;
using System.Collections.Generic;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarkovChain.Structs.Meta {
	

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
			List<NGramSuccessor> current_succesors;
			List<int> current_running;
			int current_sucind;
			int current_weight;

			List<int> seeds = new List<int>();

			void readDic(ref Utf8JsonReader r) {
				// Start array
				r.Read();

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

				// Loop must begin where token to be is start of object
				while (true) {
					r.Read();

					// Exit when at end of array (token isnt start of object)
					if (r.TokenType != JsonTokenType.StartObject) break;

					// Parse successors

					// Successor property name
					r.Read();

					// Start of successors array
					r.Read();

					current_succesors = new List<NGramSuccessor>();

					// Loop must begin where token to be is start of object
					while (true) {
						r.Read();

						// Exit at end of array (is not start object)
						if (r.TokenType != JsonTokenType.StartObject) break;

						// Index property name
						r.Read();

						// Index value
						r.Read();
						current_sucind = r.GetInt32();

						// Weight property name
						r.Read();

						// Weight value
						r.Read();
						current_weight = r.GetInt32();

						// End of NGramSuccessor object
						r.Read();

						current_succesors.Add(new NGramSuccessor(current_sucind, current_weight));
					}

					// Parse running totals

					// Running totals name
					r.Read();

					// Start of running totals array
					r.Read();

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

					links.Add(new MarkovSegment(current_succesors.ToArray(), current_running.ToArray()));
				}

				// Ends ready to parse next property
			}

			void readSeeds(ref Utf8JsonReader r) {
				// Start of array
				r.Read();

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

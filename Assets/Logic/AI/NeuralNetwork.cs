using System;
using System.Collections.Generic;
using System.Linq;

namespace Assets.Logic.AI
{
    /// <summary>
    /// Red feedforward totalmente conectada con activación tanh en todas las capas (incluida la salida).
    /// No se entrena con gradiente: el algoritmo genético le asigna los parámetros con <see cref="SetParameters"/>.
    /// </summary>
    public class NeuralNetwork
    {
        /// <summary>Neuronas por capa, incluida la de entrada. Ej: {10, 8, 2}.</summary>
        public IReadOnlyList<int> Layers { get; }

        /// <summary>Total de pesos + sesgos = longitud del genoma.</summary>
        public int ParameterCount { get; }

        private readonly float[] parameters;

        /// <summary>
        /// Requiere al menos 2 capas (entrada y salida) y al menos 1 neurona por capa;
        /// si no, lanza <see cref="ArgumentException"/>. Los parámetros arrancan en 0.
        /// </summary>
        public NeuralNetwork(params int[] layers)
        {
            if (layers is null || layers.Length < 2)
                throw new ArgumentException("The network needs at least an input and an output layer", nameof(layers));
            if (layers.Any(neurons => neurons < 1))
                throw new ArgumentException("Every layer needs at least one neuron", nameof(layers));

            Layers = layers.ToArray();
            ParameterCount = CountParameters(layers);
            parameters = new float[ParameterCount];
        }

        /// <summary>
        /// Para cada par de capas consecutivas (n entradas → m salidas) hay n·m pesos + m sesgos.
        /// Ej: {10, 8, 2} → (10·8 + 8) + (8·2 + 2) = 106.
        /// </summary>
        public static int CountParameters(IReadOnlyList<int> layers)
        {
            int count = 0;
            for (int layer = 0; layer < layers.Count - 1; layer++)
                count += layers[layer] * layers[layer + 1] + layers[layer + 1];
            return count;
        }

        /// <summary>
        /// Copia el genoma en los pesos y sesgos de la red. Si su longitud no es <see cref="ParameterCount"/>,
        /// lanza <see cref="ArgumentException"/>. La red no debe quedar enlazada al arreglo recibido.
        ///
        /// Orden del genoma, capa por capa y, dentro de cada capa, neurona de salida por neurona de salida:
        ///   [ w(entrada 0 → salida 0), w(entrada 1 → salida 0), …, w(entrada n-1 → salida 0), sesgo(salida 0),
        ///     w(entrada 0 → salida 1), …, sesgo(salida 1),
        ///     … ]
        /// </summary>
        public void SetParameters(float[] genome)
        {
            if (genome is null || genome.Length != ParameterCount)
                throw new ArgumentException($"The genome must have {ParameterCount} genes", nameof(genome));

            Array.Copy(genome, parameters, ParameterCount);
        }

        /// <summary>
        /// Propaga las entradas capa por capa: salida_j = tanh(Σ_i w_ij · entrada_i + sesgo_j).
        /// Si la cantidad de entradas no coincide con la primera capa, lanza <see cref="ArgumentException"/>.
        /// </summary>
        public float[] Feedforward(float[] inputs)
        {
            if (inputs is null || inputs.Length != Layers[0])
                throw new ArgumentException($"The network expects {Layers[0]} inputs", nameof(inputs));

            float[] activations = inputs;
            int parameter = 0;

            for (int layer = 1; layer < Layers.Count; layer++)
            {
                var next = new float[Layers[layer]];

                for (int neuron = 0; neuron < next.Length; neuron++)
                {
                    double sum = 0;
                    for (int input = 0; input < activations.Length; input++)
                        sum += parameters[parameter++] * activations[input];
                    sum += parameters[parameter++];

                    next[neuron] = (float)Math.Tanh(sum);
                }

                activations = next;
            }

            return activations;
        }
    }
}

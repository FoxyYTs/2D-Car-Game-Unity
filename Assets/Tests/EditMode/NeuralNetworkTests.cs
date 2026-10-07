using System;
using System.Linq;
using Assets.Logic.AI;
using NUnit.Framework;

namespace Assets.Tests.EditMode
{
    public class NeuralNetworkTests
    {
        private const float TOLERANCE = 1e-5f;

        [Test]
        public void CountParameters_SumsWeightsAndBiasesOfEveryLayer()
        {
            Assert.AreEqual(106, NeuralNetwork.CountParameters(new[] { 10, 8, 2 }));
            Assert.AreEqual(3, NeuralNetwork.CountParameters(new[] { 2, 1 }));
        }

        [Test]
        public void Constructor_ExposesLayersAndParameterCount()
        {
            var network = new NeuralNetwork(10, 8, 2);

            CollectionAssert.AreEqual(new[] { 10, 8, 2 }, network.Layers);
            Assert.AreEqual(106, network.ParameterCount);
        }

        [Test]
        public void Constructor_RejectsInvalidLayers()
        {
            Assert.Throws<ArgumentException>(() => new NeuralNetwork(3));
            Assert.Throws<ArgumentException>(() => new NeuralNetwork(3, 0, 2));
        }

        [Test]
        public void SetParameters_RejectsGenomeOfWrongLength()
        {
            var network = new NeuralNetwork(2, 1);

            Assert.Throws<ArgumentException>(() => network.SetParameters(new float[2]));
            Assert.Throws<ArgumentException>(() => network.SetParameters(new float[4]));
        }

        [Test]
        public void Feedforward_RejectsInputsOfWrongLength()
        {
            var network = new NeuralNetwork(2, 1);

            Assert.Throws<ArgumentException>(() => network.Feedforward(new float[3]));
        }

        [Test]
        public void Feedforward_WithZeroParameters_ReturnsZeros()
        {
            var network = new NeuralNetwork(3, 4, 2);

            var outputs = network.Feedforward(new[] { 1f, -2f, 0.5f });

            CollectionAssert.AreEqual(new[] { 0f, 0f }, outputs);
        }

        [Test]
        public void Feedforward_SingleLayer_AppliesWeightsBiasAndTanh()
        {
            var network = new NeuralNetwork(2, 1);
            network.SetParameters(new[] { 0.5f, -1f, 0.25f });

            var outputs = network.Feedforward(new[] { 1f, 2f });

            Assert.AreEqual((float)Math.Tanh(0.5 * 1 - 1 * 2 + 0.25), outputs[0], TOLERANCE);
        }

        [Test]
        public void Feedforward_FollowsGenomeLayoutForEveryOutputNeuron()
        {
            // 2 entradas → 2 salidas: [w00, w10, b0, w01, w11, b1]
            var network = new NeuralNetwork(2, 2);
            network.SetParameters(new[] { 1f, 0f, 0f, 0f, 1f, 0.5f });

            var outputs = network.Feedforward(new[] { 0.3f, -0.2f });

            Assert.AreEqual((float)Math.Tanh(0.3), outputs[0], TOLERANCE);
            Assert.AreEqual((float)Math.Tanh(-0.2 + 0.5), outputs[1], TOLERANCE);
        }

        [Test]
        public void Feedforward_ChainsHiddenLayers()
        {
            // 1 → 1 → 1: [w1, b1, w2, b2]
            var network = new NeuralNetwork(1, 1, 1);
            network.SetParameters(new[] { 2f, 0f, 3f, 0.1f });

            var outputs = network.Feedforward(new[] { 0.5f });

            Assert.AreEqual((float)Math.Tanh(3 * Math.Tanh(2 * 0.5) + 0.1), outputs[0], TOLERANCE);
        }

        [Test]
        public void Feedforward_OutputsStayWithinTanhRange()
        {
            var network = new NeuralNetwork(3, 5, 2);
            network.SetParameters(Enumerable.Repeat(50f, network.ParameterCount).ToArray());

            var outputs = network.Feedforward(new[] { 10f, 10f, 10f });

            Assert.That(outputs, Has.All.InRange(-1f, 1f));
        }

        [Test]
        public void SetParameters_CopiesTheGenome()
        {
            var network = new NeuralNetwork(2, 1);
            var genome = new[] { 0.5f, -1f, 0.25f };
            network.SetParameters(genome);
            var before = network.Feedforward(new[] { 1f, 2f })[0];

            genome[0] = 100f;

            Assert.AreEqual(before, network.Feedforward(new[] { 1f, 2f })[0], TOLERANCE);
        }
    }
}

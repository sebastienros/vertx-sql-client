/*
 * Copyright (c) 2011-2026 Contributors to the Eclipse Foundation
 *
 * SPDX-License-Identifier: EPL-2.0 OR Apache-2.0
 */

using System.Security.Cryptography;
using Apex.MySqlClient.Internal;

namespace Apex.MySqlClient.Tests;

[TestClass]
public sealed class MySqlAuthenticationTests
{
  private static readonly byte[] Nonce = Enumerable.Range(1, 20).Select(static i => (byte)i).ToArray();

  [TestMethod]
  public void ScramblesNativePasswordUsingSha1DoubleHash()
  {
    byte[] password = MySqlAuthentication.GetPasswordBytes("secret");

    byte[] scramble = MySqlAuthentication.ScrambleNativePassword(password, Nonce);

    CollectionAssert.AreEqual(
      Convert.FromHexString("B32BB3A583E1340C0A1108D58B1BE49781AD8C2F"),
      scramble);
  }

  [TestMethod]
  public void EmptyPasswordProducesEmptyNativeScramble()
  {
    byte[] scramble = MySqlAuthentication.ScrambleNativePassword([], Nonce);

    Assert.AreEqual(0, scramble.Length);
  }

  [TestMethod]
  public void ScramblesCachingSha2PasswordUsingSha256TripleHash()
  {
    byte[] password = MySqlAuthentication.GetPasswordBytes("secret");

    byte[] scramble = MySqlAuthentication.ScrambleCachingSha2Password(password, Nonce);

    CollectionAssert.AreEqual(
      Convert.FromHexString("746EBE205D56A0707ACB3E796E834E0DD7B1D61743B26BD5202C7A623230C7C9"),
      scramble);
  }

  [TestMethod]
  public void EmptyPasswordProducesEmptyCachingSha2Scramble()
  {
    byte[] scramble = MySqlAuthentication.ScrambleCachingSha2Password([], Nonce);

    Assert.AreEqual(0, scramble.Length);
  }

  [TestMethod]
  public void NullTerminatedPasswordAppendsSingleZeroByte()
  {
    byte[] password = MySqlAuthentication.GetPasswordBytes("secret");

    byte[] result = MySqlAuthentication.GetNullTerminatedPassword(password);

    Assert.AreEqual(password.Length + 1, result.Length);
    Assert.AreEqual((byte)0, result[^1]);
    CollectionAssert.AreEqual(password, result[..^1]);
  }

  [TestMethod]
  public void EncryptsPasswordWithRsaOaepUsingServerPublicKey()
  {
    using RSA rsa = RSA.Create(2048);
    string publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();
    byte[] password = MySqlAuthentication.GetPasswordBytes("secret");

    byte[] encrypted = MySqlAuthentication.EncryptPassword(password, Nonce, publicKeyPem);

    Assert.AreEqual(256, encrypted.Length);
    byte[] decrypted = rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA1);
    byte[] expectedObfuscated = MySqlAuthentication.GetNullTerminatedPassword(password);
    for (int i = 0; i < expectedObfuscated.Length; i++)
    {
      expectedObfuscated[i] ^= Nonce[i % Nonce.Length];
    }

    CollectionAssert.AreEqual(expectedObfuscated, decrypted);
  }

  [TestMethod]
  public void EncryptPasswordRejectsEmptyNonce()
  {
    using RSA rsa = RSA.Create(2048);
    string publicKeyPem = rsa.ExportSubjectPublicKeyInfoPem();

    Assert.ThrowsExactly<InvalidDataException>(() =>
      MySqlAuthentication.EncryptPassword([1, 2, 3], [], publicKeyPem));
  }

  [TestMethod]
  public void EncryptPasswordRejectsInvalidPem()
  {
    Assert.ThrowsExactly<InvalidDataException>(() =>
      MySqlAuthentication.EncryptPassword([1, 2, 3], Nonce, "not a pem"));
  }
}

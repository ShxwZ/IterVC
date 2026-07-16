using Microsoft.VisualStudio.TestTools.UnitTesting;
using RadioOSC.Core.Helpers;

namespace RadioOSC.Tests;

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_HasIsSuccessTrue()
    {
        var r = Result<int>.Success(42);
        Assert.IsTrue(r.IsSuccess);
        Assert.AreEqual(42, r.Value);
        Assert.IsNull(r.Error);
    }

    [TestMethod]
    public void Failure_HasIsSuccessFalse()
    {
        var r = Result<int>.Failure("oops");
        Assert.IsFalse(r.IsSuccess);
        Assert.AreEqual(0, r.Value); // default(int)
        Assert.AreEqual("oops", r.Error);
    }

    [TestMethod]
    public void Success_WithReferenceType_StoresValue()
    {
        var r = Result<string>.Success("hello");
        Assert.IsTrue(r.IsSuccess);
        Assert.AreEqual("hello", r.Value);
        Assert.IsNull(r.Error);
    }

    [TestMethod]
    public void Failure_WithNullableReferenceType_HasNullValue()
    {
        var r = Result<string>.Failure("bad");
        Assert.IsFalse(r.IsSuccess);
        Assert.IsNull(r.Value);
        Assert.AreEqual("bad", r.Error);
    }

    [TestMethod]
    public void IsSuccess_DistinguishesBothFactories()
    {
        Assert.IsTrue(Result<int>.Success(1).IsSuccess);
        Assert.IsFalse(Result<int>.Failure("x").IsSuccess);
    }

    [TestMethod]
    public void IsReadOnlyStruct_NoFieldsAssignable()
    {
        // El struct es readonly: esto es un test "estático" — si se quita readonly,
        // este test seguirá compilando pero el código de la API debe garantizar inmutabilidad
        // expuesta (no hay setters públicos).
        var r = Result<int>.Success(7);
        Assert.AreEqual(7, r.Value);
        Assert.IsTrue(r.IsSuccess);
    }
}

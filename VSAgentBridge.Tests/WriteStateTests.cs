using VSAgentBridge.Logic;

namespace VSAgentBridge.Tests;

[TestClass]
internal sealed class WriteStateTests
{
    [TestMethod]
    [DataRow("new")]
    [DataRow("existing notutf8")]
    [DataRow("existing bom crlf final")]
    [DataRow("existing nobom lf nofinal")]
    [DataRow("existing bom mixed final")]
    [DataRow("existing nobom mixed nofinal")]
    public void Parse_ToString_RoundTrip(string value)
    {
        var state = WriteState.Parse(value);

        Assert.IsNotNull(state);
        Assert.AreEqual(value, state.ToString());
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("old")]
    [DataRow("new bom")]
    [DataRow("existing")]
    [DataRow("existing bom crlf")]
    [DataRow("existing bom crlf final extra")]
    [DataRow("existing BOM crlf final")]
    [DataRow("existing bom cr final")]
    [DataRow("existing bom crlf eof")]
    [DataRow("existing notutf8 crlf")]
    public void Parse_NotAStateOfThisBridge_Null(string? value) =>
        Assert.IsNull(WriteState.Parse(value));

    [TestMethod]
    public void Parse_Null_Null() =>
        Assert.IsNull(WriteState.Parse(null));

    [TestMethod]
    public void Parse_ExtraSpaces_Accepted() =>
        Assert.AreEqual("existing bom crlf final", WriteState.Parse("  existing  bom crlf   final ")!.ToString());

    [TestMethod]
    public void Parse_Existing_Properties()
    {
        var state = WriteState.Parse("existing nobom mixed nofinal")!;

        Assert.IsFalse(state.IsNew);
        Assert.IsFalse(state.NotUtf8);
        Assert.IsFalse(state.Bom);
        Assert.IsNull(state.Crlf);
        Assert.IsFalse(state.FinalNewline);
    }

    [TestMethod]
    public void Parse_NewAndNotUtf8_AreTheShared()
    {
        Assert.AreSame(WriteState.New, WriteState.Parse("new"));
        Assert.AreSame(WriteState.ExistingNotUtf8, WriteState.Parse("existing notutf8"));
        Assert.IsTrue(WriteState.New.IsNew);
        Assert.IsTrue(WriteState.ExistingNotUtf8.NotUtf8);
        Assert.IsFalse(WriteState.ExistingNotUtf8.IsNew);
    }

    [TestMethod]
    public void Existing_ToString()
    {
        Assert.AreEqual("existing bom crlf final", WriteState.Existing(bom: true, crlf: true, finalNewline: true).ToString());
        Assert.AreEqual("existing nobom lf nofinal", WriteState.Existing(bom: false, crlf: false, finalNewline: false).ToString());
        Assert.AreEqual("existing bom mixed nofinal", WriteState.Existing(bom: true, crlf: null, finalNewline: false).ToString());
    }
}

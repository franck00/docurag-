using DocuRag.Core.Models;
using Xunit;

namespace DocuRag.Core.Tests;

public class AskRequestValidatorTests
{
    [Fact]
    public void Validate_ValidRequest_NoErrors()
    {
        var request = new AskRequest { Question = "Quel est le taux de TVA ?", DocumentType = "dgi", TopK = 5 };

        Assert.Empty(AskRequestValidator.Validate(request));
    }

    [Fact]
    public void Validate_NullRequest_ReturnsError()
    {
        Assert.Single(AskRequestValidator.Validate(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankQuestion_ReturnsError(string question)
    {
        var errors = AskRequestValidator.Validate(new AskRequest { Question = question });

        Assert.Contains(errors, e => e.Contains("'question'"));
    }

    [Fact]
    public void Validate_QuestionAtLimit_IsAccepted_AndAboveIsRejected()
    {
        var atLimit = new AskRequest { Question = new string('a', AskRequestValidator.MaxQuestionLength) };
        var tooLong = new AskRequest { Question = new string('a', AskRequestValidator.MaxQuestionLength + 1) };

        Assert.Empty(AskRequestValidator.Validate(atLimit));
        Assert.Contains(AskRequestValidator.Validate(tooLong), e => e.Contains("'question'"));
    }

    [Theory]
    [InlineData(0)]   // 0 = valeur par défaut appliquée par RagService
    [InlineData(1)]
    [InlineData(AskRequestValidator.MaxTopK)]
    public void Validate_TopKInRange_IsAccepted(int topK)
    {
        Assert.Empty(AskRequestValidator.Validate(new AskRequest { Question = "q", TopK = topK }));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(AskRequestValidator.MaxTopK + 1)]
    [InlineData(10_000)]
    public void Validate_TopKOutOfRange_ReturnsError(int topK)
    {
        var errors = AskRequestValidator.Validate(new AskRequest { Question = "q", TopK = topK });

        Assert.Contains(errors, e => e.Contains("'topK'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("douanes-cemac")]
    [InlineData("fiscalité")] // le type suit le nom du dossier blob, accents compris
    public void Validate_DocumentType_Accepted(string? type)
    {
        Assert.Empty(AskRequestValidator.Validate(new AskRequest { Question = "q", DocumentType = type }));
    }

    [Fact]
    public void Validate_DocumentTypeTooLong_ReturnsError()
    {
        var request = new AskRequest
        {
            Question = "q",
            DocumentType = new string('x', AskRequestValidator.MaxDocumentTypeLength + 1),
        };

        Assert.Contains(AskRequestValidator.Validate(request), e => e.Contains("'documentType'"));
    }

    [Fact]
    public void Validate_SeveralProblems_ReturnsAllErrors()
    {
        var request = new AskRequest { Question = " ", TopK = 99 };

        Assert.Equal(2, AskRequestValidator.Validate(request).Count);
    }
}

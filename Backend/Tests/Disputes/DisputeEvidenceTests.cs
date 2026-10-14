using CommonService.Application.Features.Disputes.Dtos;
using CommonService.Application.Features.Disputes.Services;
using CommonService.Application.Interfaces.Ports;
using CommonService.Domain.Enums;
using CommonService.Infrastructure.Fakes;
using CommonService.Infrastructure.Services;
using CommonService.WebAPI.Controllers.Disputes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CommonService.Tests.Disputes;

/// <summary>BE-M6-02d: the evidence photo upload and the endpoint that serves it (contract disputes.md 2.1a).</summary>
public class DisputeEvidenceTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF, 0xE0];

    private static byte[] Png(int length = 32) => Pad(PngSignature, length);

    private static byte[] Jpeg(int length = 32) => Pad(JpegStart, length);

    private static byte[] Webp() => [0x52, 0x49, 0x46, 0x46, 0x10, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x20];

    private static byte[] Pad(byte[] head, int length)
    {
        var bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static UploadEvidenceRequestDto Body(byte[] content, string type = "image/png", string name = "photo.png") => new()
    {
        FileName = name,
        ContentType = type,
        ContentBase64 = Convert.ToBase64String(content),
    };

    private sealed class FakeUser(int? id) : ICurrentUser
    {
        public bool IsAuthenticated => id is not null;
        public int? UserId => id;
        public UserRole? Role => null;
    }

    [Fact]
    public async Task A_png_is_stored_in_the_evidence_folder_with_the_customer_prefix_and_its_address_is_returned()
    {
        var storage = new FakeFileStorage();
        var png = Png(100);

        var result = await new DisputeEvidenceService(storage).UploadAsync(DisputeSide.Customer, 5, Body(png));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.StartsWith("dispute-evidence/", result.Data!.Path);
        Assert.StartsWith("/files/dispute-evidence/", result.Data.Url);
        Assert.EndsWith("c5-photo.png", result.Data.Url);
        Assert.Equal(100, result.Data.SizeBytes);
        Assert.Equal(1, storage.Count);
    }

    [Fact]
    public async Task A_worker_file_gets_the_worker_prefix_and_jpeg_and_webp_are_accepted()
    {
        var storage = new FakeFileStorage();
        var service = new DisputeEvidenceService(storage);

        var jpeg = await service.UploadAsync(DisputeSide.Worker, 9, Body(Jpeg(), "image/jpeg", "IMG_1.JPEG"));
        var webp = await service.UploadAsync(DisputeSide.Worker, 9, Body(Webp(), "IMAGE/WEBP", "x.webp"));

        Assert.EndsWith("w9-IMG_1.jpg", jpeg.Data!.Url); // the extension follows the content type, not the name
        Assert.EndsWith("w9-x.webp", webp.Data!.Url);
    }

    [Theory]
    [InlineData("..\\..\\evil/passwd.png", "c5-passwd.png")]
    [InlineData("my photo (1).png", "c5-my_photo__1_.png")]
    [InlineData("....png", "c5-photo.png")]
    public async Task Only_a_safe_file_name_is_kept(string given, string expectedEnd)
    {
        var storage = new FakeFileStorage();

        var result = await new DisputeEvidenceService(storage).UploadAsync(DisputeSide.Customer, 5, Body(Png(), name: given));

        Assert.True(result.Success);
        Assert.EndsWith(expectedEnd, result.Data!.Url);
        Assert.DoesNotContain("..", result.Data.Url);
        Assert.Equal("/files/dispute-evidence/", result.Data.Url.Substring(0, result.Data.Url.LastIndexOf('/') + 1));
    }

    [Fact]
    public async Task A_wrong_type_an_empty_photo_and_bad_base64_are_400s_with_field_messages_and_nothing_is_stored()
    {
        var storage = new FakeFileStorage();
        var service = new DisputeEvidenceService(storage);

        var html = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(), "text/html"));
        var empty = await service.UploadAsync(DisputeSide.Customer, 5, new UploadEvidenceRequestDto { FileName = "a.png", ContentType = "image/png", ContentBase64 = "" });
        var notBase64 = await service.UploadAsync(DisputeSide.Customer, 5, new UploadEvidenceRequestDto { FileName = "a.png", ContentType = "image/png", ContentBase64 = "not base64!!" });
        var nothing = await service.UploadAsync(DisputeSide.Customer, 5, new UploadEvidenceRequestDto());

        Assert.Equal(400, html.StatusCode);
        Assert.Contains("contentType", html.ValidationErrors!.Keys);
        Assert.Contains("contentBase64", empty.ValidationErrors!.Keys);
        Assert.Equal("Must be valid base64.", notBase64.ValidationErrors!["contentBase64"][0]);
        Assert.Equal(["contentType", "fileName", "contentBase64"], nothing.ValidationErrors!.Keys.ToArray());
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task The_content_must_look_like_the_declared_type()
    {
        var storage = new FakeFileStorage();
        var service = new DisputeEvidenceService(storage);

        var pngAsJpeg = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(), "image/jpeg", "a.jpg"));
        var script = await service.UploadAsync(DisputeSide.Customer, 5, Body("<script>alert(1)</script>"u8.ToArray(), "image/png"));

        Assert.Contains("not a image/jpeg", pngAsJpeg.ValidationErrors!["contentBase64"][0]);
        Assert.Contains("contentBase64", script.ValidationErrors!.Keys);
        Assert.Equal(0, storage.Count);
    }

    [Fact]
    public async Task Five_megabytes_are_accepted_and_one_byte_more_is_refused()
    {
        var storage = new FakeFileStorage();
        var service = new DisputeEvidenceService(storage);

        var ok = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(DisputeEvidenceService.MaxBytes)));
        var tooBig = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(DisputeEvidenceService.MaxBytes + 1)));

        Assert.True(ok.Success);
        Assert.Equal("The photo must be at most 5 MB.", tooBig.ValidationErrors!["contentBase64"][0]);
        Assert.Equal(1, storage.Count);
    }

    [Fact]
    public async Task A_blank_or_too_long_file_name_is_refused()
    {
        var service = new DisputeEvidenceService(new FakeFileStorage());

        var blank = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(), name: "  "));
        var tooLong = await service.UploadAsync(DisputeSide.Customer, 5, Body(Png(), name: new string('a', 101) + ".png"));

        Assert.Contains("fileName", blank.ValidationErrors!.Keys);
        Assert.Contains("fileName", tooLong.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Both_upload_actions_answer_401_without_a_user_400_without_a_body_and_201_with_a_photo()
    {
        var service = new DisputeEvidenceService(new FakeFileStorage());
        var noUser = new FakeUser(null);

        var customer = new CustomerDisputesController(DisputeEndpointTests.Filing(), service, new FakeUser(11));
        var worker = new WorkerDisputesController(DisputeEndpointTests.Filing(), service, new FakeUser(22));

        Assert.IsType<UnauthorizedObjectResult>(await new CustomerDisputesController(DisputeEndpointTests.Filing(), service, noUser).UploadEvidence(Body(Png()), default));
        Assert.IsType<UnauthorizedObjectResult>(await new WorkerDisputesController(DisputeEndpointTests.Filing(), service, noUser).UploadEvidence(Body(Png()), default));
        Assert.IsType<BadRequestObjectResult>(await customer.UploadEvidence(null!, default));
        Assert.IsType<BadRequestObjectResult>(await worker.UploadEvidence(null!, default));
        Assert.Equal(201, Assert.IsType<ObjectResult>(await customer.UploadEvidence(Body(Png()), default)).StatusCode);
        Assert.Equal(201, Assert.IsType<ObjectResult>(await worker.UploadEvidence(Body(Png()), default)).StatusCode);
    }

    private static DisputeEvidenceFilesController Files(IFileStorage storage) => new(storage)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    [Fact]
    public async Task A_stored_photo_is_served_back_with_its_image_type_and_nosniff()
    {
        var storage = new FakeFileStorage();
        var stored = await new DisputeEvidenceService(storage).UploadAsync(DisputeSide.Customer, 5, Body(Png(40)));
        var name = stored.Data!.Path["dispute-evidence/".Length..];
        var controller = Files(storage);

        var served = Assert.IsType<FileStreamResult>(await controller.Get(name, default));

        Assert.Equal("image/png", served.ContentType);
        Assert.Equal("nosniff", controller.Response.Headers["X-Content-Type-Options"]);
        using var copy = new MemoryStream();
        await served.FileStream.CopyToAsync(copy);
        Assert.Equal(40, copy.Length);
    }

    [Theory]
    [InlineData("missing.png")]
    [InlineData("page.html")]
    [InlineData("a.exe")]
    [InlineData("..png")]
    [InlineData("a..\\b.png")]
    [InlineData("a/b.png")]
    [InlineData("")]
    public async Task Anything_that_is_not_a_stored_evidence_image_is_a_404(string name)
    {
        var storage = new FakeFileStorage();
        await new DisputeEvidenceService(storage).UploadAsync(DisputeSide.Customer, 5, Body(Png()));

        Assert.IsType<NotFoundResult>(await Files(storage).Get(name, default));
    }

    [Fact]
    public async Task On_the_real_disk_storage_an_uploaded_photo_round_trips_and_a_traversal_name_is_refused()
    {
        var dir = Path.Combine(Path.GetTempPath(), "evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["FileStorage:BasePath"] = dir }).Build();
            var storage = new LocalFileStorage(config);
            var png = Png(64);

            var stored = await new DisputeEvidenceService(storage).UploadAsync(DisputeSide.Worker, 9, Body(png));

            Assert.Matches(@"^/files/dispute-evidence/[0-9a-f]{32}_w9-photo\.png$", stored.Data!.Url);
            var name = stored.Data.Url["/files/dispute-evidence/".Length..];
            var served = Assert.IsType<FileStreamResult>(await Files(storage).Get(name, default));
            using var copy = new MemoryStream();
            await served.FileStream.CopyToAsync(copy);
            await served.FileStream.DisposeAsync();
            Assert.Equal(png, copy.ToArray());

            Assert.IsType<NotFoundResult>(await Files(storage).Get("..\\..\\secret.png", default));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

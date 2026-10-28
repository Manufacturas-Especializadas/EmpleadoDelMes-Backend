using EmpleadoDelMes.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmpleadoDelMes.UnitTests;

[TestClass]
public sealed class PingControllerTests
{
    [TestMethod]
    public void Get_DevuelveHttp200YContenido()
    {
        // Arrange: preparar el objeto
        var controller = new PingController();

        // Act: ejecutar el método
        var response = controller.Get();

        // Assert: comprobar el resultado
        var okResult = response as OkObjectResult;

        Assert.IsNotNull(okResult);
        Assert.AreEqual(200, okResult.StatusCode);
        Assert.IsNotNull(okResult.Value);
    }
}
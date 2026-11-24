using AppEmocions.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace AppEmocions.Api.Controllers;

// Endpoints para la app móvil (Android). Todo lo que sube la app pasa por aquí.
[ApiController]
[Route("api/movil")]
public class MovilController : ControllerBaseDatos
{
    public MovilController(Db db) : base(db) { }

    // Datos que envía la app al terminar un nivel.
    public class SesionMovilDto
    {
        public string Codigo { get; set; } = "";      // nombre de usuario de la app = participante.codigo
        public string? Grupo { get; set; }            // grupo si el participante es nuevo (por defecto P4-A)
        public int Nivel { get; set; }                // 1..4
        public int DuracionS { get; set; }            // segundos que ha durado
        public decimal Puntuacion { get; set; }
        public int Intentos { get; set; }
        public int Errores { get; set; }
        public int Ayudas { get; set; }
        public int Aciertos { get; set; }
        public int? RespMediaMs { get; set; }         // tiempo medio de respuesta en ms
        public string? Dificultad { get; set; }       // "facil"/"dificil": de momento no se guarda (la BD no tiene columna)
        public bool Completada { get; set; } = true;
        public bool Simulada { get; set; }            // true para datos de prueba
    }

    // POST: api/movil/sesion
    [HttpPost("sesion")]
    public IActionResult GuardarSesion([FromBody] SesionMovilDto d)
    {
        if (string.IsNullOrWhiteSpace(d.Codigo) || d.Codigo.Length > 50)
            return BadRequest("Codigo obligatorio (máx. 50 caracteres).");
        if (d.Nivel < 1 || d.Nivel > 4)
            return BadRequest("Nivel debe estar entre 1 y 4.");
        if (d.DuracionS < 0 || d.Intentos < 0 || d.Errores < 0 || d.Ayudas < 0 || d.Aciertos < 0)
            return BadRequest("Los contadores no pueden ser negativos.");

        var sesionId = Guid.NewGuid().ToString();
        var ahoraUtc = DateTime.UtcNow;
        var inicioUtc = ahoraUtc.AddSeconds(-d.DuracionS);
        var inicioLocal = DateTime.Now.AddSeconds(-d.DuracionS);

        // Todo en un único lote con transacción: o se guarda todo o nada.
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRAN;

            DECLARE @pid INT = (SELECT participante_id FROM dbo.participante WHERE codigo = @codigo);
            IF @pid IS NULL
            BEGIN
                INSERT INTO dbo.participante (codigo, grupo) VALUES (@codigo, @grupo);
                SET @pid = SCOPE_IDENTITY();
            END

            INSERT INTO dbo.sesion
                (sesion_id, participante_id, juego_id, schema_version, iniciada_en, iniciada_utc, fecha_local,
                 duracion_s, puntuacion, nivel_alcanzado, intentos, errores, ayudas, completada, abandonada, simulada, aciertos, resp_media_ms)
            VALUES
                (@sesion, @pid, 1, 1, @inicioLocal, @inicioUtc, CAST(@inicioLocal AS date),
                 @dur, @punt, @nivel, @intentos, @errores, @ayudas, @completada, 0, @simulada, @aciertos, @resp);

            INSERT INTO dbo.nivel_sesion
                (sesion_id, nivel, completado, duracion_ms, intentos, errores, ayudas, aciertos, resp_media_ms)
            VALUES
                (@sesion, @nivel, @completada, @dur * 1000, @intentos, @errores, @ayudas, @aciertos, @resp);

            COMMIT;";

        try
        {
            _db.Ejecutar(sql,
                new SqlParameter("@codigo", d.Codigo.Trim()),
                new SqlParameter("@grupo", string.IsNullOrWhiteSpace(d.Grupo) ? "P4-A" : d.Grupo),
                new SqlParameter("@sesion", sesionId),
                new SqlParameter("@inicioLocal", inicioLocal),
                new SqlParameter("@inicioUtc", inicioUtc),
                new SqlParameter("@dur", d.DuracionS),
                new SqlParameter("@punt", d.Puntuacion),
                new SqlParameter("@nivel", (byte)d.Nivel),
                new SqlParameter("@intentos", d.Intentos),
                new SqlParameter("@errores", d.Errores),
                new SqlParameter("@ayudas", d.Ayudas),
                new SqlParameter("@completada", d.Completada),
                new SqlParameter("@simulada", d.Simulada),
                new SqlParameter("@aciertos", d.Aciertos),
                new SqlParameter("@resp", (object?)d.RespMediaMs ?? DBNull.Value));
        }
        catch (SqlException ex)
        {
            return StatusCode(500, "Error al guardar en la base de datos: " + ex.Message);
        }

        return Ok(new { sesion_id = sesionId });
    }

    // GET: api/movil/sesiones/{codigo}  -> historial de un participante (para la pantalla "Mis partidas")
    [HttpGet("sesiones/{codigo}")]
    public IActionResult SesionesDe(string codigo)
    {
        const string sql = @"
            SELECT s.sesion_id, s.iniciada_en, s.duracion_s, s.puntuacion, s.nivel_alcanzado, s.errores, s.aciertos
            FROM dbo.sesion s
            INNER JOIN dbo.participante p ON p.participante_id = s.participante_id
            WHERE p.codigo = @codigo
            ORDER BY s.iniciada_en DESC;";

        return Ok(Convertir(_db.Consultar(sql, new SqlParameter("@codigo", codigo))));
    }
}

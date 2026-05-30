using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PJATK_APBD_Cw8_s34072.DTOs;
using PJATK_APBD_Cw8_s34072.Models;

namespace PJATK_APBD_Cw8_s34072.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly HospitalContext _context;

    public PatientsController(HospitalContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetPatients([FromQuery] string? search)
    {
        var query = _context.Patients
            .Include(p => p.Admissions)
                .ThenInclude(a => a.Ward)
            .Include(p => p.BedAssignments)
                .ThenInclude(ba => ba.Bed)
                    .ThenInclude(b => b.BedType)
            .Include(p => p.BedAssignments)
                .ThenInclude(ba => ba.Bed)
                    .ThenInclude(b => b.Room)
                        .ThenInclude(r => r.Ward)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(p => EF.Functions.Like(p.FirstName, $"%{search}%") ||
                                     EF.Functions.Like(p.LastName, $"%{search}%"));
        }

        var patients = await query.Select(p => new PatientDto
        {
            Pesel = p.Pesel,
            FirstName = p.FirstName,
            LastName = p.LastName,
            Age = p.Age,
            Sex = p.Sex ? "Male" : "Female",
            Admissions = p.Admissions.Select(a => new AdmissionDto
            {
                Id = a.Id,
                AdmissionDate = a.AdmissionDate,
                DischargeDate = a.DischargeDate,
                Ward = new WardDto
                {
                    Id = a.Ward.Id,
                    Name = a.Ward.Name,
                    Description = a.Ward.Description
                }
            }).ToList(),
            BedAssignments = p.BedAssignments.Select(ba => new BedAssignmentGetDto
            {
                Id = ba.Id,
                From = ba.From,
                To = ba.To,
                Bed = new BedDto
                {
                    Id = ba.BedId,
                    BedType = new BedTypeDto
                    {
                        Id = ba.Bed.BedType.Id,
                        Name = ba.Bed.BedType.Name,
                        Description = ba.Bed.BedType.Description
                    },
                    Room = new RoomDto
                    {
                        Id = ba.Bed.Room.Id,
                        HasTv = ba.Bed.Room.HasTv,
                        Ward = new WardDto
                        {
                            Id = ba.Bed.Room.Ward.Id,
                            Name = ba.Bed.Room.Ward.Name,
                            Description = ba.Bed.Room.Ward.Description
                        }
                    }
                }
            }).ToList()
        }).ToListAsync();

        return Ok(patients);
    }

    [HttpPost("{pesel}/bedassignments")]
    public async Task<IActionResult> AssignBed(string pesel, [FromBody] AssignBedRequestDto request)
    {
        var patientExists = await _context.Patients.AnyAsync(p => p.Pesel == pesel);
        if (!patientExists)
        {
            return NotFound($"Pacjent o numerze PESEL {pesel} nie istnieje w bazie danych.");
        }

        // Pozbywamy się DateTime.MaxValue i sprawdzamy nulle bezpośrednio w LINQ
        bool isRequestToNull = !request.To.HasValue;
        DateTime requestToValue = request.To ?? DateTime.MinValue;

        var availableBed = await _context.Beds
            .Include(b => b.BedType)
            .Include(b => b.Room)
            .ThenInclude(r => r.Ward)
            .Where(b => b.BedType.Name == request.BedType && b.Room.Ward.Name == request.Ward)
            .Where(b => !b.BedAssignments.Any(ba =>
                // Logika sprawdzająca nakładanie się dat (bez wysyłania MaxValue do SQL Servera)
                (isRequestToNull || ba.From < requestToValue) && 
                (ba.To == null || request.From < ba.To)))
            .FirstOrDefaultAsync();

        if (availableBed == null)
        {
            return NotFound($"Brak wolnego łóżka typu '{request.BedType}' na oddziale '{request.Ward}' we wskazanym przedziale czasowym.");
        }

        var newAssignment = new BedAssignment
        {
            PatientPesel = pesel,
            BedId = availableBed.Id,
            From = request.From,
            To = request.To
        };

        _context.BedAssignments.Add(newAssignment);
        await _context.SaveChangesAsync();

        return StatusCode(201);
    }
}
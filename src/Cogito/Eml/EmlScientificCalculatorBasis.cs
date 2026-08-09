// SPDX-License-Identifier: MIT
using System.Numerics;

namespace Cogito;

/// The paper's Calc-4 scientific-calculator basis. These are reference functions only; the catalog deliberately
/// carries none of the paper's known EML programs. PaperK -1 denotes a target absent from the leaf-count table.
internal static class EmlScientificCalculatorBasis
{
    internal static bool IsGivenTerminal(EmlTarget target)
        => target.Label is "1" or "x" or "y";

    internal static EmlTarget[] CreateTargets() =>
    [
        new("pi", EmlCats.Constant, 53, true, (x, y) => new Complex(Math.PI, 0)),
        new("e", EmlCats.Constant, 3, false, (x, y) => new Complex(Math.E, 0)),
        new("i", EmlCats.Constant, 55, true, (x, y) => Complex.ImaginaryOne),
        new("-1", EmlCats.Constant, 15, false, (x, y) => -Complex.One),
        new("1", EmlCats.Constant, 1, false, (x, y) => Complex.One),
        new("2", EmlCats.Constant, 19, false, (x, y) => new Complex(2, 0)),
        new("x", EmlCats.Constant, 9, false, (x, y) => x),
        new("y", EmlCats.Constant, -1, false, (x, y) => y),

        new("exp", EmlCats.Function, 3, false, (x, y) => Complex.Exp(x)),
        new("ln", EmlCats.Function, 7, false, (x, y) => Complex.Log(x)),
        new("inv", EmlCats.Function, 15, false, (x, y) => Complex.One / x),
        new("x/2", EmlCats.Function, 27, false, (x, y) => x / 2),
        new("neg", EmlCats.Function, 15, false, (x, y) => -x),
        new("sqrt", EmlCats.Function, 43, false, (x, y) => Complex.Sqrt(x)),
        new("x^2", EmlCats.Function, 17, false, (x, y) => x * x),
        new("sigmoid", EmlCats.Function, -1, false, (x, y) => Complex.One / (Complex.One + Complex.Exp(-x))),
        new("sin", EmlCats.Function, -1, false, (x, y) => Complex.Sin(x)),
        new("cos", EmlCats.Function, -1, false, (x, y) => Complex.Cos(x)),
        new("tan", EmlCats.Function, -1, false, (x, y) => Complex.Tan(x)),
        new("asin", EmlCats.Function, -1, false, (x, y) => Complex.Asin(x)),
        new("acos", EmlCats.Function, -1, false, (x, y) => Complex.Acos(x)),
        new("atan", EmlCats.Function, -1, false, (x, y) => Complex.Atan(x)),
        new("sinh", EmlCats.Function, -1, false, (x, y) => Complex.Sinh(x)),
        new("cosh", EmlCats.Function, -1, false, (x, y) => Complex.Cosh(x)),
        new("tanh", EmlCats.Function, -1, false, (x, y) => Complex.Tanh(x)),
        new("asinh", EmlCats.Function, -1, false,
            (x, y) => Complex.Log(x + Complex.Sqrt(x * x + Complex.One))),
        new("acosh", EmlCats.Function, -1, false,
            (x, y) => Complex.Log(x + Complex.Sqrt(x + Complex.One) * Complex.Sqrt(x - Complex.One))),
        new("atanh", EmlCats.Function, -1, false,
            (x, y) => (Complex.Log(Complex.One + x) - Complex.Log(Complex.One - x)) / 2),

        new("x+y", EmlCats.Operator, 19, false, (x, y) => x + y),
        new("x-y", EmlCats.Operator, 11, false, (x, y) => x - y),
        new("x*y", EmlCats.Operator, 17, false, (x, y) => x * y),
        new("x/y", EmlCats.Operator, 17, false, (x, y) => x / y),
        new("log_xy", EmlCats.Operator, 29, false, (x, y) => Complex.Log(y) / Complex.Log(x)),
        new("x^y", EmlCats.Operator, 25, false, (x, y) => Complex.Pow(x, y)),
        new("avg", EmlCats.Operator, 27, true, (x, y) => (x + y) / 2),
        new("hypot", EmlCats.Operator, 27, true, (x, y) => Complex.Sqrt(x * x + y * y)),
    ];
}

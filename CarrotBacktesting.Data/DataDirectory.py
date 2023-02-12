import os


class DataDirectory:
    UnitTestDataDirectory = '../CarrotBackTesting.Net.UnitTest/TestData/'

    @staticmethod
    def combine2(d1: str, d2: str) -> str:
        return os.path.join(d1, d2)

    @staticmethod
    def combine3(d1: str, d2: str, d3: str) -> str:
        return os.path.join(d1, d2, d3)

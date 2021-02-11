﻿using Silk.Extensions;
using Xunit;
namespace Silk.Extensions.Tests
{
    
    public class StringExtensionMethods
    {
        private const string BaseInputString = "This string should center!";
        
        [Fact]
        public void CenterWithoutTabs()
        {
            //Arrange
            const string inputAnchorWithoutTabs = "This is a test string that should be centered against!";
            const string expectedWithoutTabs = "              This string should center!              ";

            //Act
            string actualCenterWithoutTabs = BaseInputString.Center(inputAnchorWithoutTabs);

            
            //Assert
            Assert.Equal(expectedWithoutTabs, actualCenterWithoutTabs);
            
        }

        [Fact]
        public void CenterWithTabs()
        {
            //Arange
            const string inputAnchorWithTabs = "This is a\ttest string that\tshould be centered!";
            const string expectedWithTabs = "             This string should center!             ";
            
            //Act
            string actualCenterWithTabs = BaseInputString.Center(inputAnchorWithTabs);
            
            //Assert
            Assert.Equal(expectedWithTabs, actualCenterWithTabs);
        }
    }
}